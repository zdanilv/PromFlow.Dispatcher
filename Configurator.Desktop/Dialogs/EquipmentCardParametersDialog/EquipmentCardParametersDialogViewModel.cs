using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using ReactiveUI;

namespace Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;

public sealed class EquipmentCardParametersDialogViewModel : ReactiveObject, IDisposable
{
    private readonly IEquipmentCommandDispatcher _dispatcher;
    private readonly IEquipmentParameterWriteValidator _writeValidator;
    private readonly IEquipmentParameterValueStore _valueStore;
    private readonly IEquipmentCardParameterDefinitionStore? _definitionStore;
    private readonly EquipmentCommandCard _card;
    private readonly IReadOnlyDictionary<string, SignalValue>? _signals;
    private readonly bool _isConnectionAvailable;
    private readonly Subject<bool> _result = new();
    private string? _errorMessage;
    private string? _statusMessage;
    private string? _createErrorMessage;
    private bool _isAddingParameter;
    private bool _isCreatingParameter;
    private bool _isSavingValues;
    private string? _newParameterTitle;
    private string? _newParameterSignalId;
    private SignalBindingDirection _newParameterDirection = SignalBindingDirection.ReadWrite;
    private SignalValueType _newParameterValueType = SignalValueType.Word;
    private EquipmentParameterVisibility _newParameterVisibility = EquipmentParameterVisibility.User;
    private bool _disposed;

    public EquipmentCardParametersDialogViewModel(
        EquipmentCommandCard card,
        IReadOnlyDictionary<string, SignalValue>? signals,
        IEquipmentCommandDispatcher dispatcher,
        IEquipmentParameterWriteValidator? writeValidator = null,
        bool showTechnicalDetails = true,
        bool isConnectionAvailable = true,
        IEquipmentParameterValueStore? valueStore = null,
        bool isAdminMode = true,
        IEquipmentCardParameterDefinitionStore? definitionStore = null)
    {
        _card = card;
        _signals = signals;
        _dispatcher = dispatcher;
        _writeValidator = writeValidator ?? NoopEquipmentParameterWriteValidator.Instance;
        _valueStore = valueStore ?? NoopEquipmentParameterValueStore.Instance;
        _definitionStore = definitionStore;
        _isConnectionAvailable = isConnectionAvailable;
        IsAdminMode = isAdminMode;
        ShowTechnicalDetails = showTechnicalDetails;
        Title = card.Title;
        var visibleParameters = card.Parameters.Where(parameter =>
            EquipmentCardParameter.IsVisible(parameter.Visibility, isAdminMode));
        foreach (var parameter in visibleParameters)
            Parameters.Add(new EquipmentCardParameterRow(parameter, signals, showTechnicalDetails));

        Parameters.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasNoParameters));
        var hasParameters = Observable.FromEventPattern<NotifyCollectionChangedEventHandler, NotifyCollectionChangedEventArgs>(
                handler => Parameters.CollectionChanged += handler,
                handler => Parameters.CollectionChanged -= handler)
            .Select(_ => !HasNoParameters)
            .StartWith(!HasNoParameters);
        var propertyChanges = Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
            handler => PropertyChanged += handler,
            handler => PropertyChanged -= handler);
        var isAdding = propertyChanges.Where(change => change.EventArgs.PropertyName == nameof(IsAddingParameter))
            .Select(_ => IsAddingParameter).StartWith(IsAddingParameter);
        var isCreating = propertyChanges.Where(change => change.EventArgs.PropertyName == nameof(IsCreatingParameter))
            .Select(_ => IsCreatingParameter).StartWith(IsCreatingParameter);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync,
            hasParameters.CombineLatest(isAdding, isCreating, (hasRows, adding, creating) => hasRows && !adding && !creating));
        AddCommand = ReactiveCommand.Create(OpenAddForm,
            isAdding.CombineLatest(isCreating, SaveCommand.IsExecuting,
                (adding, creating, saving) => CanAddParameter && !adding && !creating && !saving));
        var canEditDraft = isAdding.CombineLatest(isCreating, (adding, creating) => CanAddParameter && adding && !creating);
        CreateCommand = ReactiveCommand.CreateFromTask(CreateParameterAsync, canEditDraft);
        CancelAddCommand = ReactiveCommand.Create(CancelAdd, canEditDraft);
        CloseCommand = ReactiveCommand.Create(Close, isCreating.Select(creating => !creating));
    }

    public string Title { get; }
    public bool ShowTechnicalDetails { get; }
    public bool IsAdminMode { get; }
    public bool CanAddParameter => IsAdminMode && _definitionStore is not null;
    public string SignalIdPlaceholder => $"{_card.Id}.parameter…";
    public IReadOnlyList<SignalBindingDirection> DirectionChoices { get; } = Enum.GetValues<SignalBindingDirection>();
    public IReadOnlyList<SignalValueType> ValueTypeChoices { get; } = Enum.GetValues<SignalValueType>();
    public IReadOnlyList<RouteMapEditorChoice<EquipmentParameterVisibility>> VisibilityChoices { get; } =
    [
        new(EquipmentParameterVisibility.User, "Для пользователя"),
        new(EquipmentParameterVisibility.Admin, "Только для администратора"),
    ];
    public ObservableCollection<EquipmentCardParameterRow> Parameters { get; } = [];
    public bool HasNoParameters => Parameters.Count == 0;
    public string EmptyStateMessage => "Для текущего режима нет доступных настроек оборудования.";
    public IObservable<bool> Result => _result;
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> AddCommand { get; }
    public ReactiveCommand<Unit, Unit> CreateCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelAddCommand { get; }

    public bool IsAddingParameter
    {
        get => _isAddingParameter;
        private set => this.RaiseAndSetIfChanged(ref _isAddingParameter, value);
    }

    public bool IsCreatingParameter
    {
        get => _isCreatingParameter;
        private set => this.RaiseAndSetIfChanged(ref _isCreatingParameter, value);
    }

    public string? NewParameterTitle
    {
        get => _newParameterTitle;
        set => this.RaiseAndSetIfChanged(ref _newParameterTitle, value);
    }

    public string? NewParameterSignalId
    {
        get => _newParameterSignalId;
        set => this.RaiseAndSetIfChanged(ref _newParameterSignalId, value);
    }

    public SignalBindingDirection NewParameterDirection
    {
        get => _newParameterDirection;
        set => this.RaiseAndSetIfChanged(ref _newParameterDirection, value);
    }

    public SignalValueType NewParameterValueType
    {
        get => _newParameterValueType;
        set => this.RaiseAndSetIfChanged(ref _newParameterValueType, value);
    }

    public EquipmentParameterVisibility NewParameterVisibility
    {
        get => _newParameterVisibility;
        set => this.RaiseAndSetIfChanged(ref _newParameterVisibility, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? CreateErrorMessage
    {
        get => _createErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _createErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasCreateError));
        }
    }

    public bool HasCreateError => !string.IsNullOrWhiteSpace(CreateErrorMessage);

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _result.Dispose();
    }

    private void OpenAddForm()
    {
        if (_disposed || !CanAddParameter || IsAddingParameter || IsCreatingParameter || _isSavingValues)
            return;

        ErrorMessage = null;
        StatusMessage = null;
        IsAddingParameter = true;
    }

    private void ClearDraft()
    {
        IsAddingParameter = false;
        CreateErrorMessage = null;
        NewParameterTitle = null;
        NewParameterSignalId = null;
        NewParameterDirection = SignalBindingDirection.ReadWrite;
        NewParameterValueType = SignalValueType.Word;
        NewParameterVisibility = EquipmentParameterVisibility.User;
    }

    private void CancelAdd()
    {
        if (!IsAddingParameter || IsCreatingParameter)
            return;

        ClearDraft();
    }

    private async Task CreateParameterAsync()
    {
        if (_disposed || !IsAdminMode || _definitionStore is null || !IsAddingParameter || IsCreatingParameter)
            return;

        CreateErrorMessage = null;
        if (string.IsNullOrWhiteSpace(NewParameterTitle))
        {
            CreateErrorMessage = "Название настройки оборудования обязательно.";
            return;
        }

        if (string.IsNullOrWhiteSpace(NewParameterSignalId))
        {
            CreateErrorMessage = "SignalId настройки оборудования обязателен.";
            return;
        }

        if (!Enum.IsDefined(NewParameterDirection) || !Enum.IsDefined(NewParameterValueType) ||
            !Enum.IsDefined(NewParameterVisibility))
        {
            CreateErrorMessage = "Проверьте направление, тип значения и видимость параметра.";
            return;
        }

        var definition = new NewEquipmentCardParameterDefinition(
            NewParameterTitle.Trim(), NewParameterSignalId.Trim(), NewParameterDirection,
            NewParameterValueType, NewParameterVisibility);
        IsCreatingParameter = true;
        try
        {
            var parameter = await _definitionStore.AddAsync(_card.Id, definition);
            if (_disposed)
                return;

            if (EquipmentCardParameter.IsVisible(parameter.Visibility, IsAdminMode))
                Parameters.Add(new EquipmentCardParameterRow(parameter, _signals, ShowTechnicalDetails));

            ClearDraft();
            StatusMessage = "Настройка добавлена";
        }
        catch (OperationCanceledException)
        {
            if (!_disposed)
                CreateErrorMessage = "Создание настройки отменено.";
        }
        catch (Exception ex)
        {
            if (!_disposed)
                CreateErrorMessage = $"Не удалось добавить настройку: {ex.Message}";
        }
        finally
        {
            if (!_disposed)
                IsCreatingParameter = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_disposed || IsAddingParameter || IsCreatingParameter || _isSavingValues)
            return;

        _isSavingValues = true;
        try
        {
            await SaveValuesAsync();
        }
        finally
        {
            _isSavingValues = false;
        }
    }

    private async Task SaveValuesAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        var requests = new List<(EquipmentCardParameterRow Parameter, SignalWriteRequest Request)>();
        foreach (var parameter in Parameters)
        {
            parameter.ValidationMessage = null;
            if (!parameter.CanEdit || !parameter.HasSetpoint)
                continue;

            if (parameter.TryCreateRequest(out var request, out var validationMessage))
            {
                requests.Add((parameter, request));
            }
            else
            {
                parameter.ValidationMessage = validationMessage;
            }
        }

        if (_isConnectionAvailable)
        {
            foreach (var item in requests)
                item.Parameter.ValidationMessage = _writeValidator.Validate(item.Request);
        }

        if (Parameters.Any(parameter => parameter.HasValidationError))
        {
            ErrorMessage = "Исправьте значения параметров.";
            return;
        }

        if (requests.Count == 0)
        {
            StatusMessage = Parameters.Any(parameter => parameter.CanEdit)
                ? "Нет заданных значений для сохранения."
                : "Нет редактируемых параметров.";
            return;
        }

        try
        {
            var values = requests
                .Select(item => new EquipmentParameterSetpoint(
                    item.Request.SignalId,
                    item.Parameter.SerializeValue(item.Request)))
                .ToArray();

            await _valueStore.SaveAsync(
                _card.Id,
                values,
                pendingAutoDispatch: !_isConnectionAvailable);

            if (!_isConnectionAvailable)
            {
                StatusMessage = "Сохранено локально. Отправка будет выполнена после подключения.";
                return;
            }

            var failures = new List<string>();
            var sent = 0;
            foreach (var item in requests)
            {
                var savedValue = item.Parameter.SerializeValue(item.Request);
                try
                {
                    await _dispatcher.DispatchAsync(item.Request);
                    await _valueStore.CompleteDispatchAsync(_card.Id, item.Request.SignalId, savedValue, null);
                    sent++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await _valueStore.CompleteDispatchAsync(_card.Id, item.Request.SignalId, savedValue, ex.Message);
                    failures.Add($"{item.Parameter.Title}: {ex.Message}");
                }
            }

            StatusMessage = $"Отправлено параметров: {sent}.";
            if (failures.Count > 0)
                ErrorMessage = $"Не удалось сохранить параметры: {string.Join("; ", failures)}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = $"Не удалось сохранить параметры: {ex.Message}";
        }
    }

    private void Close()
    {
        if (_disposed || IsCreatingParameter)
            return;

        CancelAdd();
        _result.OnNext(false);
    }
}

internal sealed class NoopEquipmentParameterValueStore : IEquipmentParameterValueStore
{
    public static NoopEquipmentParameterValueStore Instance { get; } = new();

    public Task SaveAsync(
        string cardId,
        IReadOnlyCollection<EquipmentParameterSetpoint> values,
        bool pendingAutoDispatch,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public IReadOnlyList<PendingEquipmentParameter> GetPending() => [];

    public Task CompleteDispatchAsync(
        string cardId,
        string signalId,
        string expectedSavedValue,
        string? errorMessage,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class EquipmentCardParameterRow : ReactiveObject
{
    private readonly bool _requiresSetpointInput;
    private string _valueText;
    private bool _boolValue;
    private bool? _explicitBoolValue;
    private bool _hasEditedText;
    private string? _validationMessage;

    public EquipmentCardParameterRow(
        EquipmentCardParameter parameter,
        IReadOnlyDictionary<string, SignalValue>? signals,
        bool showTechnicalDetails = true)
    {
        Title = parameter.Title;
        Binding = parameter.Binding;
        ShowTechnicalDetails = showTechnicalDetails;
        DispatchError = parameter.LastDispatchError;
        // A live value is not a setpoint for a newly defined parameter, including after reopening the dialog.
        _requiresSetpointInput = parameter.RequiresExplicitSetpoint && CanEdit && parameter.SavedValue is null;
        _valueText = _requiresSetpointInput ? string.Empty : InitialValue(parameter, signals);
        _boolValue = InitialBoolValue(parameter, signals);
    }

    public string Title { get; }
    public SignalBinding Binding { get; }
    public string SignalId => Binding.SignalId;
    public SignalBindingDirection Direction => Binding.Direction;
    public SignalValueType ValueType => Binding.ValueType;
    public string SignalCaption => $"{SignalId} • {ValueType}";
    public bool ShowTechnicalDetails { get; }
    public bool IsBool => ValueType == SignalValueType.Bool;
    public bool UsesBoolToggle => IsBool && !_requiresSetpointInput;
    public bool UsesExplicitBoolInput => IsBool && _requiresSetpointInput;
    public IReadOnlyList<RouteMapEditorChoice<bool>> BoolChoices => ExplicitBoolChoices;
    private static readonly IReadOnlyList<RouteMapEditorChoice<bool>> ExplicitBoolChoices =
        [new(false, "Выкл"), new(true, "Вкл")];
    public bool UsesTextInput => !IsBool;
    public bool CanEdit => Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite;
    internal bool HasSetpoint => !_requiresSetpointInput || (IsBool ? ExplicitBoolValue.HasValue : _hasEditedText);
    public bool IsReadOnly => !CanEdit;
    public string? DispatchError { get; }
    public bool HasDispatchError => !string.IsNullOrWhiteSpace(DispatchError);

    public string ValueText
    {
        get => _valueText;
        set
        {
            var text = value ?? string.Empty;
            if (text != _valueText)
                _hasEditedText = true;
            this.RaiseAndSetIfChanged(ref _valueText, text);
            ValidationMessage = null;
        }
    }

    public bool BoolValue
    {
        get => _boolValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _boolValue, value);
            ValidationMessage = null;
        }
    }

    public bool? ExplicitBoolValue
    {
        get => _explicitBoolValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _explicitBoolValue, value);
            ValidationMessage = null;
        }
    }

    public string? ValidationMessage
    {
        get => _validationMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _validationMessage, value);
            this.RaisePropertyChanged(nameof(HasValidationError));
        }
    }

    public bool HasValidationError => !string.IsNullOrWhiteSpace(ValidationMessage);

    public bool TryCreateRequest(out SignalWriteRequest request, out string? validationMessage)
    {
        request = new SignalWriteRequest(SignalId, null, ValueType);
        if (!CanEdit || !HasSetpoint)
        {
            validationMessage = null;
            return false;
        }

        if (ValueType == SignalValueType.Bool)
        {
            var boolValue = UsesExplicitBoolInput ? ExplicitBoolValue.GetValueOrDefault() : BoolValue;
            request = new SignalWriteRequest(SignalId, boolValue, ValueType);
            validationMessage = null;
            return true;
        }

        if (!TryParse(ValueText, ValueType, out var value, out validationMessage))
            return false;

        request = new SignalWriteRequest(SignalId, value, ValueType);
        return true;
    }

    public string SerializeValue(SignalWriteRequest request)
    {
        if (request.ValueType == SignalValueType.Bool)
            return (request.Value is true).ToString().ToLowerInvariant();

        if (request.ValueType == SignalValueType.Date && request.Value is DateTime dateTime)
            return dateTime.ToString("O", CultureInfo.InvariantCulture);

        if (request.Value is float float32)
            return float32.ToString("R", CultureInfo.InvariantCulture);

        if (request.Value is double float64)
            return float64.ToString("R", CultureInfo.InvariantCulture);

        if (request.Value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return request.Value?.ToString() ?? string.Empty;
    }

    private static string InitialValue(
        EquipmentCardParameter parameter,
        IReadOnlyDictionary<string, SignalValue>? signals)
    {
        var binding = parameter.Binding;
        if (binding.ValueType == SignalValueType.Bool)
        {
            return string.Empty;
        }

        if (binding.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite &&
            parameter.SavedValue is not null)
        {
            return parameter.SavedValue;
        }

        if (binding.Direction == SignalBindingDirection.Write ||
            signals is null ||
            !signals.TryGetValue(binding.SignalId, out var signal) ||
            !signal.IsQualityGood ||
            signal.IsStale)
        {
            return string.Empty;
        }

        return FormatValue(signal.Value, binding.ValueType);
    }

    private static bool InitialBoolValue(
        EquipmentCardParameter parameter,
        IReadOnlyDictionary<string, SignalValue>? signals)
    {
        var binding = parameter.Binding;
        if (binding.ValueType == SignalValueType.Bool &&
            binding.Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite &&
            bool.TryParse(parameter.SavedValue, out var savedValue))
        {
            return savedValue;
        }

        if (binding.ValueType != SignalValueType.Bool ||
            binding.Direction == SignalBindingDirection.Write ||
            signals is null ||
            !signals.TryGetValue(binding.SignalId, out var signal) ||
            !signal.IsQualityGood ||
            signal.IsStale ||
            signal.Value is not bool boolean)
        {
            return false;
        }

        return boolean;
    }

    private static string FormatValue(object? value, SignalValueType valueType)
    {
        if (value is null)
            return string.Empty;

        if (valueType == SignalValueType.Bool && value is bool boolean)
            return boolean ? "true" : "false";

        if (valueType == SignalValueType.Date && value is DateTime dateTime)
            return dateTime.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

        if (valueType == SignalValueType.Date && value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.CurrentCulture);

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.CurrentCulture)
            : value.ToString() ?? string.Empty;
    }

    private static bool TryParse(
        string text,
        SignalValueType valueType,
        out object? value,
        out string? validationMessage)
    {
        value = null;
        validationMessage = null;
        var normalized = text.Trim();

        if (valueType != SignalValueType.String && string.IsNullOrWhiteSpace(normalized))
        {
            validationMessage = "Значение обязательно.";
            return false;
        }

        switch (valueType)
        {
            case SignalValueType.Int16:
                if (TryParseNumber(normalized, short.TryParse, out short int16))
                {
                    value = int16;
                    return true;
                }
                validationMessage = "Введите число Int16.";
                return false;
            case SignalValueType.UInt16:
                if (TryParseNumber(normalized, ushort.TryParse, out ushort uint16))
                {
                    value = uint16;
                    return true;
                }
                validationMessage = "Введите число UInt16.";
                return false;
            case SignalValueType.Word:
                if (TryParseNumber(normalized, ushort.TryParse, out ushort word))
                {
                    value = word;
                    return true;
                }
                validationMessage = "Введите число WORD в диапазоне 0..65535.";
                return false;
            case SignalValueType.Int32:
                if (TryParseNumber(normalized, int.TryParse, out int int32))
                {
                    value = int32;
                    return true;
                }
                validationMessage = "Введите число Int32.";
                return false;
            case SignalValueType.Dword:
                if (TryParseNumber(normalized, uint.TryParse, out uint dword))
                {
                    value = dword;
                    return true;
                }
                validationMessage = "Введите число DWORD в диапазоне 0..4294967295.";
                return false;
            case SignalValueType.Float32:
                if (TryParseFloat(normalized, out var float32))
                {
                    value = float32;
                    return true;
                }
                validationMessage = "Введите число Float32.";
                return false;
            case SignalValueType.String:
                value = text;
                return true;
            case SignalValueType.Date:
                if (TryParseDate(normalized, out var date))
                {
                    value = date;
                    return true;
                }
                validationMessage = "Введите дату и время.";
                return false;
            case SignalValueType.Bool:
                validationMessage = "Bool-параметр задается переключателем Вкл/Выкл.";
                return false;
            default:
                validationMessage = $"Тип {valueType} не поддерживается.";
                return false;
        }
    }

    private delegate bool NumberParser<T>(
        string text,
        NumberStyles style,
        IFormatProvider provider,
        out T value);

    private static bool TryParseNumber<T>(
        string text,
        NumberParser<T> parser,
        out T value)
    {
        return parser(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
               parser(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseFloat(string text, out float value)
    {
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        return false;
    }

    private static bool TryParseDate(string text, out DateTime value)
    {
        return DateTime.TryParse(
                   text,
                   CultureInfo.CurrentCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                   out value) ||
               DateTime.TryParse(
                   text,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                   out value);
    }
}
