using Configurator.Application.Services.Signals;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Avalonia;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using System.Reactive;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class EquipmentCardViewModel : ViewModelBase
{
    public const string EmptyRoutePointText = "—";

    private readonly IEquipmentCommandDispatcher _commandDispatcher;
    private readonly IEquipmentCardParametersDialogService? _parametersDialogService;
    private readonly Func<IReadOnlyDictionary<string, SignalValue>?>? _signalSnapshotAccessor;
    private readonly EquipmentCommandCard _card;
    private readonly SignalBinding? _startBinding;
    private readonly SignalBinding? _stopBinding;
    private readonly SignalBinding? _uncheckedBinding;
    private readonly SignalBinding? _checkedBinding;
    private readonly bool _hasPowerFeedback;
    private readonly RouteMapPaletteSettings _palette;
    private readonly bool _usesDefaultPalette;
    private string _statusText;
    private string _sendPointTitle = EmptyRoutePointText;
    private string _returnPointTitle = EmptyRoutePointText;
    private RouteObjectState _state;
    private bool _canStart;
    private bool _canStop;
    private bool _isStartChecked;
    private bool _isStopChecked;
    private bool _isStartPressed;
    private bool _isStartHovered;
    private bool _isStopPressed;
    private bool _isStopHovered;
    private bool _isSelectorChecked;
    private bool _isSelectorPressed;
    private bool _isEnabled = true;
    private bool _isSelectorCommandEnabled = true;
    private bool _isConnectionAvailable;
    private bool _isSelectorResetActive;
    private bool _runtimeVisible = true;
    private bool _isApplyingRuntime;
    private bool? _isPowerOn;
    private bool _isStartBusy;
    private bool _isStopPending;
    private bool? _pendingStopValue;
    private bool _stopWriteInFlight;
    private bool _stopReadbackMatched;
    private Task? _stopConfirmationTask;
    private int _stopConfirmationVersion;
    private readonly TimeSpan _stopConfirmationTimeout;
    private string? _commandErrorMessage;

    public EquipmentCardViewModel(
        EquipmentCommandCard card,
        IEquipmentCommandDispatcher commandDispatcher,
        RouteMapPaletteSettings? palette = null,
        IEquipmentCardParametersDialogService? parametersDialogService = null,
        Func<IReadOnlyDictionary<string, SignalValue>?>? signalSnapshotAccessor = null,
        TimeSpan? stopConfirmationTimeout = null)
    {
        _card = card;
        Id = card.Id;
        Title = card.Title;
        IsStaticallyVisible = card.IsVisible;
        Style = card.Style ?? new EquipmentCardStyle();
        StartButtonKind = card.StartButtonKind;
        StopButtonKind = card.StopButtonKind;
        StartOffFeedbackEnabled = card.StartOffFeedbackEnabled;
        StopOffFeedbackEnabled = card.StopOffFeedbackEnabled;
        _usesDefaultPalette = palette is null;
        _palette = palette ?? new RouteMapPaletteSettings();
        _statusText = card.StatusText;
        _state = card.State;
        _canStart = card.CanStart;
        _canStop = card.CanStop;
        _commandDispatcher = commandDispatcher;
        _parametersDialogService = parametersDialogService;
        _signalSnapshotAccessor = signalSnapshotAccessor;
        _stopConfirmationTimeout = stopConfirmationTimeout ?? TimeSpan.FromSeconds(10);
        if (_stopConfirmationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(stopConfirmationTimeout));
        _startBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.StartCommand);
        _stopBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.StopCommand);
        _uncheckedBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.UncheckedCommand);
        _checkedBinding = card.Bindings.FirstOrDefault(x => x.Role == SignalBindingRole.CheckedCommand);
        _hasPowerFeedback = card.Bindings.Any(x => x.Role == SignalBindingRole.PowerFeedback);
        StartCommand = ReactiveCommand.CreateFromTask(ExecuteStartAsync);
        OpenParametersCommand = ReactiveCommand.CreateFromTask(OpenParametersAsync);
    }

    public string Id { get; }
    public string Title { get; }
    public bool IsStaticallyVisible { get; }
    public EquipmentCardStyle Style { get; }
    public RouteCommandButtonKind StartButtonKind { get; }
    public RouteCommandButtonKind StopButtonKind { get; }
    public bool StartOffFeedbackEnabled { get; }
    public bool StopOffFeedbackEnabled { get; }
    public bool IsStartToggle => StartButtonKind == RouteCommandButtonKind.Toggle;
    public bool IsStartMomentary => StartButtonKind == RouteCommandButtonKind.Momentary;
    public bool IsStopToggle => StopButtonKind == RouteCommandButtonKind.Toggle;
    public bool HasPowerFeedback => _hasPowerFeedback;
    public bool IsStartBusy
    {
        get => _isStartBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isStartBusy, value);
            this.RaisePropertyChanged(nameof(CanStart));
        }
    }
    public bool IsStopPending
    {
        get => _isStopPending;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isStopPending, value);
            this.RaisePropertyChanged(nameof(CanStop));
        }
    }
    public string? CommandErrorMessage
    {
        get => _commandErrorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _commandErrorMessage, value);
            this.RaisePropertyChanged(nameof(HasCommandError));
        }
    }
    public bool HasCommandError => !string.IsNullOrWhiteSpace(CommandErrorMessage);
    public IBrush PowerIndicatorBackground => RouteMapPalette.Brush(_isPowerOn switch
    {
        true => "#00D107",
        false => "#D10000",
        _ => "#3F474D"
    });
    public IBrush PowerIndicatorForeground => RouteMapPalette.Brush("#FFFFFF");
    public string PowerIndicatorToolTip => _isPowerOn switch
    {
        true => "Питание включено",
        false => "Питание выключено",
        _ => "Состояние питания неизвестно"
    };
    public bool IsVisible => IsStaticallyVisible && _runtimeVisible;
    public double CardWidth => Style.Width;
    public double MinimumCardWidth => Style.MinimumWidth;
    public double CardHeight => Style.Height;
    public Thickness CardMargin => ToThickness(Style.Margin);
    public Thickness CardPadding => ToThickness(Style.Padding);
    public Thickness CardBorderThickness => ToThickness(Style.BorderThickness);
    public CornerRadius CardCornerRadius => new(Style.CornerRadius.TopLeft, Style.CornerRadius.TopRight, Style.CornerRadius.BottomRight, Style.CornerRadius.BottomLeft);
    public IBrush BackgroundBrush => RouteMapPalette.Brush(Style.BackgroundColor);
    public IBrush BorderBrush => RouteMapPalette.Brush(Style.BorderColor);
    public IBrush TitleBrush => IsEnabled ? RouteMapPalette.Brush(Style.TitleColor) : DisabledBrush;
    public IBrush TextBrush => IsEnabled ? RouteMapPalette.Brush(Style.TextColor) : DisabledBrush;
    public double TitleFontSize => Style.TitleFontSize;
    public double StatusFontSize => Style.StatusFontSize;
    public double RouteTextFontSize => Style.RouteTextFontSize;
    public double ActionFontSize => Style.ActionFontSize;
    public string StartText => Style.StartText;
    public string StopText => Style.StopText;
    public IBrush StartBackground => !IsEnabled
        ? DisabledBrush
        : RouteMapPalette.Brush(RouteMapCommandButtonPalette.Start.Background(IsStartPressed, IsStartChecked, IsStartHovered));
    public IBrush StartForeground => RouteMapPalette.Brush(!IsEnabled
        ? "#FFFFFF"
        : RouteMapCommandButtonPalette.Start.Foreground(IsStartPressed, IsStartChecked));
    public IBrush StopBackground => !IsEnabled
        ? DisabledBrush
        : RouteMapPalette.Brush(RouteMapCommandButtonPalette.Emergency.Background(IsStopPressed, IsStopChecked, IsStopHovered));
    public IBrush StopForeground => RouteMapPalette.Brush(!IsEnabled
        ? "#FFFFFF"
        : RouteMapCommandButtonPalette.Emergency.Foreground(IsStopPressed, IsStopChecked));
    public IBrush SelectorBackground => !IsSelectorEnabled
        ? DisabledBrush
        : RouteMapPalette.Brush(RouteMapCommandButtonPalette.Selector.Background(IsSelectorPressed, IsSelectorChecked));
    public IBrush SelectorForeground => RouteMapPalette.Brush(!IsSelectorEnabled
        ? "#FFFFFF"
        : RouteMapCommandButtonPalette.Selector.Foreground(IsSelectorPressed, IsSelectorChecked));
    public bool IsParametersButtonEnabled => true;
    public IBrush ParameterBackground => RouteMapPalette.Brush("#D0D0D0");
    public IBrush ParameterForeground => RouteMapPalette.Brush("#101820");
    public ReactiveCommand<Unit, Unit> OpenParametersCommand { get; }
    public ReactiveCommand<Unit, Unit> StartCommand { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (_isEnabled == value)
                return;

            this.RaiseAndSetIfChanged(ref _isEnabled, value);
            RaiseEnabledVisualProperties();
        }
    }

    public bool IsSelectorEnabled
    {
        get => _isSelectorCommandEnabled;
        private set
        {
            if (_isSelectorCommandEnabled == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSelectorCommandEnabled, value);
            this.RaisePropertyChanged(nameof(SelectorBackground));
            this.RaisePropertyChanged(nameof(SelectorForeground));
        }
    }

    public string SendPointTitle
    {
        get => _sendPointTitle;
        private set
        {
            if (_sendPointTitle == value)
                return;

            this.RaiseAndSetIfChanged(ref _sendPointTitle, value);
            this.RaisePropertyChanged(nameof(SendPointText));
        }
    }

    public string ReturnPointTitle
    {
        get => _returnPointTitle;
        private set
        {
            if (_returnPointTitle == value)
                return;

            this.RaiseAndSetIfChanged(ref _returnPointTitle, value);
            this.RaisePropertyChanged(nameof(ReturnPointText));
        }
    }

    public string SendPointText => $"{Style.SendPrefix}: {SendPointTitle}";
    public string ReturnPointText => $"{Style.ReturnPrefix}: {ReturnPointTitle}";

    public string StatusText
    {
        get => _statusText;
        set
        {
            this.RaiseAndSetIfChanged(ref _statusText, value);
            this.RaisePropertyChanged(nameof(StatusBrush));
        }
    }

    public RouteObjectState State
    {
        get => _state;
        set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(StateBrush));
            this.RaisePropertyChanged(nameof(IsSelectorEnabled));
        }
    }

    public bool CanStart
    {
        get => _canStart && !IsStartBusy;
        set => this.RaiseAndSetIfChanged(ref _canStart, value);
    }

    public bool CanStop
    {
        get => _canStop && !IsStopPending;
        set => this.RaiseAndSetIfChanged(ref _canStop, value);
    }

    public bool IsStartChecked
    {
        get => _isStartChecked;
        set
        {
            if (_isStartChecked == value)
                return;

            if (IsStartMomentary)
                return;
            SetStartChecked(value);
            if (!_isApplyingRuntime)
                RequestLegacyStart(value);
        }
    }

    public bool IsStopChecked
    {
        get => _isStopChecked;
        set
        {
            if (_isStopChecked == value)
                return;

            if (_isApplyingRuntime)
            {
                SetStopChecked(value);
                if (_pendingStopValue == value)
                {
                    _stopReadbackMatched = true;
                    if (!_stopWriteInFlight)
                    {
                        _pendingStopValue = null;
                        _stopConfirmationVersion++;
                        IsStopPending = false;
                    }
                }
                return;
            }

            // Checked is readback state. An operator request stays pending until a fresh snapshot agrees.
            this.RaisePropertyChanged(nameof(IsStopChecked));
            if (CanStop)
                RequestStop(value);
        }
    }

    public void RequestStopChange(bool value)
    {
        if (value != IsStopChecked && CanStop)
            RequestStop(value);
    }

    public bool IsSelectorChecked
    {
        get => _isSelectorChecked;
        set
        {
            if (_isSelectorChecked == value)
                return;

            SetSelectorChecked(value);
            if (!_isApplyingRuntime && IsSelectorEnabled && !HasPowerFeedback)
                RequestSelector(value);
        }
    }

    public bool IsStartPressed
    {
        get => _isStartPressed;
        set
        {
            if (_isStartPressed == value)
                return;

            this.RaiseAndSetIfChanged(ref _isStartPressed, value);
            this.RaisePropertyChanged(nameof(StartBackground));
            this.RaisePropertyChanged(nameof(StartForeground));
        }
    }

    public bool IsStartHovered
    {
        get => _isStartHovered;
        set
        {
            if (_isStartHovered == value)
                return;

            this.RaiseAndSetIfChanged(ref _isStartHovered, value);
            this.RaisePropertyChanged(nameof(StartBackground));
            this.RaisePropertyChanged(nameof(StartForeground));
        }
    }

    public bool IsStopPressed
    {
        get => _isStopPressed;
        set
        {
            if (_isStopPressed == value)
                return;

            this.RaiseAndSetIfChanged(ref _isStopPressed, value);
            this.RaisePropertyChanged(nameof(StopBackground));
            this.RaisePropertyChanged(nameof(StopForeground));
        }
    }

    public bool IsStopHovered
    {
        get => _isStopHovered;
        set
        {
            if (_isStopHovered == value)
                return;

            this.RaiseAndSetIfChanged(ref _isStopHovered, value);
            this.RaisePropertyChanged(nameof(StopBackground));
            this.RaisePropertyChanged(nameof(StopForeground));
        }
    }

    public bool IsSelectorPressed
    {
        get => _isSelectorPressed;
        set
        {
            if (_isSelectorPressed == value)
                return;

            this.RaiseAndSetIfChanged(ref _isSelectorPressed, value);
            this.RaisePropertyChanged(nameof(SelectorBackground));
            this.RaisePropertyChanged(nameof(SelectorForeground));
        }
    }

    public IBrush StateBrush => State switch
    {
        RouteObjectState.Ready => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        RouteObjectState.Running => PaletteBrush(_palette.Running, RouteMapPalette.RunningBrush),
        RouteObjectState.ActiveRoute => PaletteBrush(_palette.ActiveTrack, RouteMapPalette.TrackActiveBrush),
        RouteObjectState.Warning => PaletteBrush(_palette.Warning, RouteMapPalette.WarningBrush),
        RouteObjectState.Fault => PaletteBrush(_palette.Fault, RouteMapPalette.FaultBrush),
        RouteObjectState.Offline => PaletteBrush(_palette.Offline, RouteMapPalette.OfflineBrush),
        RouteObjectState.Disabled => PaletteBrush(_palette.Disabled, RouteMapPalette.DisabledBrush),
        _ => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
    };

    public IBrush StatusBrush => !IsEnabled ? DisabledBrush : StatusText switch
    {
        "Ожидание" => PaletteBrush(_palette.Warning, RouteMapPalette.WarningBrush),
        "Выключено" => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
        "Выключен" => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
        "Не в сети" => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
        "Авария" => PaletteBrush(_palette.Fault, RouteMapPalette.FaultBrush),
        "Выполнение" => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        "Выгрузка" => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        "Загрузка" => PaletteBrush(_palette.Ready, RouteMapPalette.ReadyBrush),
        _ => PaletteBrush(_palette.MutedText, RouteMapPalette.MutedTextBrush),
    };

    public void ApplyRuntime(RouteObjectRuntimeState? runtimeState, bool isConnectionAvailable = true)
    {
        IsConnectionAvailable = isConnectionAvailable;
        if (runtimeState is null)
        {
            if (HasPowerFeedback && _isPowerOn is not null)
            {
                _isPowerOn = null;
                this.RaisePropertyChanged(nameof(PowerIndicatorBackground));
                this.RaisePropertyChanged(nameof(PowerIndicatorToolTip));
            }
            return;
        }

        _isApplyingRuntime = true;
        try
        {
            State = runtimeState.State;
            StatusText = runtimeState.Text ?? StatusText;
            CanStart = runtimeState.CanStart;
            CanStop = runtimeState.CanStop;
            IsEnabled = runtimeState.IsEnabled;
            IsSelectorEnabled = runtimeState.IsSelectorCommandEnabled;
            if (IsStartToggle)
                IsStartChecked = runtimeState.IsStartChecked && !runtimeState.IsStopChecked;
            IsStopChecked = runtimeState.IsStopChecked;
            if (HasPowerFeedback)
            {
                var powerOn = isConnectionAvailable && runtimeState.State != RouteObjectState.Offline
                    ? runtimeState.IsPowerOn
                    : null;
                if (_isPowerOn != powerOn)
                {
                    _isPowerOn = powerOn;
                    this.RaisePropertyChanged(nameof(PowerIndicatorBackground));
                    this.RaisePropertyChanged(nameof(PowerIndicatorToolTip));
                }
            }
            else
            {
                IsSelectorChecked = runtimeState.ShouldResetSelectorCommands
                    ? false
                    : runtimeState.IsSelectorChecked;
            }
            _runtimeVisible = runtimeState.IsVisible;
            this.RaisePropertyChanged(nameof(IsVisible));
        }
        finally
        {
            _isApplyingRuntime = false;
        }

        if (!HasPowerFeedback)
        {
            var shouldDispatchSelectorReset = runtimeState.ShouldResetSelectorCommands && !_isSelectorResetActive;
            _isSelectorResetActive = runtimeState.ShouldResetSelectorCommands;
            if (shouldDispatchSelectorReset)
                RequestSelectorReset();
        }
    }

    public void ApplyRouteSelection(
        IReadOnlyList<RouteNode> chainNodes,
        IReadOnlyDictionary<string, RouteNodeRoleState> roleStates)
    {
        SendPointTitle = SelectedPointTitle(chainNodes, roleStates, x => x.IsTarget);
        ReturnPointTitle = SelectedPointTitle(chainNodes, roleStates, x => x.IsLoader);
    }

    private Task DispatchAsync(SignalBinding? binding, bool value, ModbusWriteMode? requiredWriteMode = null)
    {
        if (binding is null)
            return Task.CompletedTask;

        return _commandDispatcher.DispatchAsync(
            new SignalWriteRequest(binding.SignalId, value, binding.ValueType) { RequiredWriteMode = requiredWriteMode });
    }

    private Task OpenParametersAsync()
    {
        return _parametersDialogService?.ShowAsync(_card, _signalSnapshotAccessor?.Invoke(), _isConnectionAvailable)
            ?? Task.CompletedTask;
    }

    private async Task ExecuteStartAsync()
    {
        if (!IsStartMomentary || !CanStart || IsStartBusy)
            return;
        if (_startBinding is null)
        {
            CommandErrorMessage = "ПУСК не настроен в SignalId карточки.";
            return;
        }

        IsStartBusy = true;
        CommandErrorMessage = null;
        try
        {
            await DispatchAsync(_startBinding, true, ModbusWriteMode.Pulse);
        }
        catch (Exception ex)
        {
            await UpdateUiAsync(() => CommandErrorMessage = $"ПУСК не подтверждён: {ex.Message}");
        }
        finally
        {
            await UpdateUiAsync(() => IsStartBusy = false);
        }
    }

    private void RequestLegacyStart(bool value) => _ = DispatchObservedAsync(_startBinding, value, "ПУСК");

    private void RequestStop(bool value) => _ = SetStopAsync(value);

    private async Task SetStopAsync(bool value)
    {
        if (_stopBinding is null)
        {
            CommandErrorMessage = "СТОП не настроен в SignalId карточки.";
            return;
        }
        _pendingStopValue = value;
        _stopReadbackMatched = false;
        _stopWriteInFlight = true;
        IsStopPending = true;
        CommandErrorMessage = null;
        try
        {
            await DispatchAsync(_stopBinding, value, IsStopToggle ? ModbusWriteMode.Latched : null);
            await UpdateUiAsync(() =>
            {
                _stopWriteInFlight = false;
                if (_stopReadbackMatched)
                {
                    _pendingStopValue = null;
                    _stopConfirmationVersion++;
                    IsStopPending = false;
                }
                else
                {
                    var version = ++_stopConfirmationVersion;
                    _stopConfirmationTask = ConfirmStopReadbackAsync(value, version);
                }
            });
        }
        catch (Exception ex)
        {
            await UpdateUiAsync(() =>
            {
                _pendingStopValue = null;
                _stopWriteInFlight = false;
                _stopConfirmationVersion++;
                IsStopPending = false;
                CommandErrorMessage = $"СТОП не подтверждён: {ex.Message}";
                this.RaisePropertyChanged(nameof(IsStopChecked));
            });
        }
    }

    private async Task ConfirmStopReadbackAsync(bool requestedValue, int version)
    {
        try
        {
            await Task.Delay(_stopConfirmationTimeout);
            await UpdateUiAsync(() =>
            {
                if (version != _stopConfirmationVersion || _pendingStopValue != requestedValue)
                    return;

                _pendingStopValue = null;
                _stopConfirmationVersion++;
                IsStopPending = false;
                CommandErrorMessage = "СТОП не подтверждён свежим состоянием PLC в отведённое время.";
                this.RaisePropertyChanged(nameof(IsStopChecked));
            });
        }
        catch (Exception ex)
        {
            await UpdateUiAsync(() =>
            {
                if (version == _stopConfirmationVersion)
                {
                    _pendingStopValue = null;
                    _stopConfirmationVersion++;
                    IsStopPending = false;
                    CommandErrorMessage = $"СТОП не подтверждён: {ex.Message}";
                }
            });
        }
    }

    private async Task DispatchObservedAsync(SignalBinding? binding, bool value, string commandName)
    {
        try
        {
            await DispatchAsync(binding, value);
        }
        catch (Exception ex)
        {
            await UpdateUiAsync(() => CommandErrorMessage = $"{commandName} не подтверждён: {ex.Message}");
        }
    }

    private static async Task UpdateUiAsync(Action update)
    {
        if (Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess())
            update();
        else
            await Dispatcher.UIThread.InvokeAsync(update);
    }

    private async Task DispatchSelectorAsync(bool isChecked)
    {
        if (isChecked)
        {
            await DispatchAsync(_uncheckedBinding, false);
            await DispatchAsync(_checkedBinding, true);
            return;
        }

        await DispatchAsync(_checkedBinding, false);
        await DispatchAsync(_uncheckedBinding, true);
    }

    private async Task DispatchSelectorResetAsync()
    {
        await DispatchAsync(_checkedBinding, false);
        await DispatchAsync(_uncheckedBinding, false);
    }

    private void RequestSelector(bool value) => _ = DispatchSelectorObservedAsync(value);

    private void RequestSelectorReset() => _ = DispatchSelectorResetObservedAsync();

    private async Task DispatchSelectorObservedAsync(bool value)
    {
        try { await DispatchSelectorAsync(value); }
        catch (Exception ex) { await UpdateUiAsync(() => CommandErrorMessage = $"С не подтверждена: {ex.Message}"); }
    }

    private async Task DispatchSelectorResetObservedAsync()
    {
        try { await DispatchSelectorResetAsync(); }
        catch (Exception ex) { await UpdateUiAsync(() => CommandErrorMessage = $"Сброс С не подтверждён: {ex.Message}"); }
    }

    private void SetStartChecked(bool value)
    {
        if (_isStartChecked == value)
            return;

        this.RaiseAndSetIfChanged(ref _isStartChecked, value, nameof(IsStartChecked));
        this.RaisePropertyChanged(nameof(StartBackground));
        this.RaisePropertyChanged(nameof(StartForeground));
    }

    private void SetStopChecked(bool value)
    {
        if (_isStopChecked == value)
            return;

        this.RaiseAndSetIfChanged(ref _isStopChecked, value, nameof(IsStopChecked));
        this.RaisePropertyChanged(nameof(StopBackground));
        this.RaisePropertyChanged(nameof(StopForeground));
    }

    private void SetSelectorChecked(bool value)
    {
        if (_isSelectorChecked == value)
            return;

        this.RaiseAndSetIfChanged(ref _isSelectorChecked, value, nameof(IsSelectorChecked));
        this.RaisePropertyChanged(nameof(SelectorBackground));
        this.RaisePropertyChanged(nameof(SelectorForeground));
    }

    private void RaiseEnabledVisualProperties()
    {
        this.RaisePropertyChanged(nameof(TitleBrush));
        this.RaisePropertyChanged(nameof(TextBrush));
        this.RaisePropertyChanged(nameof(StatusBrush));
        this.RaisePropertyChanged(nameof(StartBackground));
        this.RaisePropertyChanged(nameof(StartForeground));
        this.RaisePropertyChanged(nameof(StopBackground));
        this.RaisePropertyChanged(nameof(StopForeground));
        this.RaisePropertyChanged(nameof(SelectorBackground));
        this.RaisePropertyChanged(nameof(SelectorForeground));
        this.RaisePropertyChanged(nameof(IsSelectorEnabled));
    }

    private bool IsConnectionAvailable
    {
        get => _isConnectionAvailable;
        set
        {
            if (_isConnectionAvailable == value)
                return;

            this.RaiseAndSetIfChanged(ref _isConnectionAvailable, value);
        }
    }

    private static string SelectedPointTitle(
        IEnumerable<RouteNode> chainNodes,
        IReadOnlyDictionary<string, RouteNodeRoleState> roleStates,
        Func<RouteNodeRoleState, bool> predicate)
    {
        var node = chainNodes.FirstOrDefault(x =>
            roleStates.TryGetValue(x.Id, out var state) && predicate(state));

        return node is null
            ? EmptyRoutePointText
            : NormalizeTitle(node.Title);
    }

    private static string NormalizeTitle(string title)
    {
        return string.Join(" ", title.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    private static Thickness ToThickness(RouteThickness value) =>
        new(value.Left, value.Top, value.Right, value.Bottom);

    private IBrush DisabledBrush => PaletteBrush(_palette.Disabled, RouteMapPalette.DisabledBrush);

    private IBrush PaletteBrush(string color, IBrush defaultBrush) =>
        _usesDefaultPalette ? defaultBrush : RouteMapPalette.Brush(color);
}
