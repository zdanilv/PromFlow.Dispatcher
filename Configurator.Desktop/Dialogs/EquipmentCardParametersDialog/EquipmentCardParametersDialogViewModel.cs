using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive;
using System.Reactive.Subjects;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Models;
using ReactiveUI;

namespace Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;

public sealed class EquipmentCardParametersDialogViewModel : ReactiveObject, IDisposable
{
    private readonly IEquipmentCommandDispatcher _dispatcher;
    private readonly IEquipmentParameterWriteValidator _writeValidator;
    private readonly Subject<bool> _result = new();
    private string? _errorMessage;
    private string? _statusMessage;
    private bool _disposed;

    public EquipmentCardParametersDialogViewModel(
        EquipmentCommandCard card,
        IReadOnlyDictionary<string, SignalValue>? signals,
        IEquipmentCommandDispatcher dispatcher,
        IEquipmentParameterWriteValidator? writeValidator = null,
        bool showTechnicalDetails = true)
    {
        _dispatcher = dispatcher;
        _writeValidator = writeValidator ?? NoopEquipmentParameterWriteValidator.Instance;
        ShowTechnicalDetails = showTechnicalDetails;
        Title = card.Title;
        foreach (var parameter in card.Parameters)
            Parameters.Add(new EquipmentCardParameterRow(parameter, signals, showTechnicalDetails));

        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        CloseCommand = ReactiveCommand.Create(Close);
    }

    public string Title { get; }
    public bool ShowTechnicalDetails { get; }
    public ObservableCollection<EquipmentCardParameterRow> Parameters { get; } = [];
    public IObservable<bool> Result => _result;
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

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

    private async Task SaveAsync()
    {
        ErrorMessage = null;
        StatusMessage = null;

        var requests = new List<(EquipmentCardParameterRow Parameter, SignalWriteRequest Request)>();
        foreach (var parameter in Parameters)
        {
            parameter.ValidationMessage = null;
            if (!parameter.CanEdit)
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

        foreach (var item in requests)
        {
            item.Parameter.ValidationMessage = _writeValidator.Validate(item.Request);
        }

        if (Parameters.Any(parameter => parameter.HasValidationError))
        {
            ErrorMessage = "Исправьте значения параметров.";
            return;
        }

        if (requests.Count == 0)
        {
            StatusMessage = "Нет редактируемых параметров.";
            return;
        }

        try
        {
            foreach (var item in requests)
                await _dispatcher.DispatchAsync(item.Request);

            StatusMessage = $"Отправлено параметров: {requests.Count}.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ErrorMessage = $"Не удалось сохранить параметры: {ex.Message}";
        }
    }

    private void Close() => _result.OnNext(false);
}

public sealed class EquipmentCardParameterRow : ReactiveObject
{
    private string _valueText;
    private bool _boolValue;
    private string? _validationMessage;

    public EquipmentCardParameterRow(
        EquipmentCardParameter parameter,
        IReadOnlyDictionary<string, SignalValue>? signals,
        bool showTechnicalDetails = true)
    {
        Title = parameter.Title;
        Binding = parameter.Binding;
        ShowTechnicalDetails = showTechnicalDetails;
        _valueText = InitialValue(parameter.Binding, signals);
        _boolValue = InitialBoolValue(parameter.Binding, signals);
    }

    public string Title { get; }
    public SignalBinding Binding { get; }
    public string SignalId => Binding.SignalId;
    public SignalBindingDirection Direction => Binding.Direction;
    public SignalValueType ValueType => Binding.ValueType;
    public string SignalCaption => $"{SignalId} • {ValueType}";
    public bool ShowTechnicalDetails { get; }
    public bool IsBool => ValueType == SignalValueType.Bool;
    public bool UsesTextInput => !IsBool;
    public bool CanEdit => Direction is SignalBindingDirection.Write or SignalBindingDirection.ReadWrite;
    public bool IsReadOnly => !CanEdit;

    public string ValueText
    {
        get => _valueText;
        set
        {
            this.RaiseAndSetIfChanged(ref _valueText, value);
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
        if (!CanEdit)
        {
            validationMessage = null;
            return false;
        }

        if (ValueType == SignalValueType.Bool)
        {
            request = new SignalWriteRequest(SignalId, BoolValue, ValueType);
            validationMessage = null;
            return true;
        }

        if (!TryParse(ValueText, ValueType, out var value, out validationMessage))
            return false;

        request = new SignalWriteRequest(SignalId, value, ValueType);
        return true;
    }

    private static string InitialValue(
        SignalBinding binding,
        IReadOnlyDictionary<string, SignalValue>? signals)
    {
        if (binding.ValueType == SignalValueType.Bool)
        {
            return string.Empty;
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
        SignalBinding binding,
        IReadOnlyDictionary<string, SignalValue>? signals)
    {
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
