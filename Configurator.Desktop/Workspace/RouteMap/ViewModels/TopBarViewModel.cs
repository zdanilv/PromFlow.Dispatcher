using Avalonia.Media;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Dialogs.HelpDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class TopBarViewModel : ViewModelBase
{
    private readonly IEquipmentCommandDispatcher? _commandDispatcher;
    private readonly IHelpDialogService? _helpDialogService;
    private RouteTopBarSettings _settings;
    private string _disabledColor;
    private bool _isAutomaticMode;
    private bool _isManualMode = true;
    private bool _isResetActive;
    private bool _hasEmergency;
    private bool _isAutomaticPressed;
    private bool _isManualPressed;
    private bool _isResetPressed;
    private bool _isEmergencyPressed;
    private bool _isResetHovered;
    private bool _isEmergencyHovered;
    private bool _isAutomaticCommandEnabled = true;
    private bool _isManualCommandEnabled = true;
    private bool _isResetCommandEnabled = true;
    private bool _isEmergencyCommandEnabled = true;

    public TopBarViewModel(
        IRouteMapSettingsDialogService? settingsDialogService = null,
        IEquipmentCommandDispatcher? commandDispatcher = null,
        RouteTopBarSettings? settings = null,
        bool isSettingsVisible = true,
        RouteMapPaletteSettings? palette = null,
        IHelpDialogService? helpDialogService = null)
    {
        _commandDispatcher = commandDispatcher;
        _helpDialogService = helpDialogService;
        _settings = settings ?? CreateDefaultSettings();
        _disabledColor = (palette ?? new RouteMapPaletteSettings()).Disabled;
        IsSettingsVisible = isSettingsVisible;

        SwitchToAutomaticCommand = ReactiveCommand.CreateFromTask(SwitchToAutomaticAsync);
        SwitchToManualCommand = ReactiveCommand.CreateFromTask(SwitchToManualAsync);
        ResetCommand = ReactiveCommand.CreateFromTask(ExecuteResetAsync);
        EmergencyCommand = ReactiveCommand.CreateFromTask(ExecuteEmergencyAsync);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(() =>
            settingsDialogService?.ShowAsync() ?? Task.CompletedTask);
        OpenHelpCommand = ReactiveCommand.CreateFromTask(() =>
            _helpDialogService?.ShowAsync() ?? Task.CompletedTask);
    }

    public bool IsAutomaticMode
    {
        get => _isAutomaticMode;
        private set => this.RaiseAndSetIfChanged(ref _isAutomaticMode, value);
    }

    public bool IsManualMode
    {
        get => _isManualMode;
        private set => this.RaiseAndSetIfChanged(ref _isManualMode, value);
    }

    public bool HasEmergency
    {
        get => _hasEmergency;
        private set => this.RaiseAndSetIfChanged(ref _hasEmergency, value);
    }

    public bool IsResetActive
    {
        get => _isResetActive;
        private set => this.RaiseAndSetIfChanged(ref _isResetActive, value);
    }

    public bool IsAutomaticCommandEnabled
    {
        get => _isAutomaticCommandEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isAutomaticCommandEnabled, value);
    }

    public bool IsManualCommandEnabled
    {
        get => _isManualCommandEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isManualCommandEnabled, value);
    }

    public bool IsEmergencyCommandEnabled
    {
        get => _isEmergencyCommandEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isEmergencyCommandEnabled, value);
    }

    public bool IsResetCommandEnabled
    {
        get => _isResetCommandEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isResetCommandEnabled, value);
    }

    public bool AreCommandsEnabled =>
        IsAutomaticCommandEnabled && IsManualCommandEnabled && IsResetCommandEnabled && IsEmergencyCommandEnabled;

    public bool IsSettingsVisible { get; }

    public string AutomaticText => _settings.Automatic.Text;
    public string ManualText => _settings.Manual.Text;
    public string ResetText => _settings.Reset.Text;
    public string EmergencyText => _settings.Emergency.Text;
    public IBrush AutomaticBackground => CommandBrush(RouteMapCommandButtonPalette.Mode, IsAutomaticMode, _isAutomaticPressed, isHovered: false, IsAutomaticCommandEnabled, foreground: false);
    public IBrush AutomaticForeground => CommandBrush(RouteMapCommandButtonPalette.Mode, IsAutomaticMode, _isAutomaticPressed, isHovered: false, IsAutomaticCommandEnabled, foreground: true);
    public IBrush ManualBackground => CommandBrush(RouteMapCommandButtonPalette.Mode, IsManualMode, _isManualPressed, isHovered: false, IsManualCommandEnabled, foreground: false);
    public IBrush ManualForeground => CommandBrush(RouteMapCommandButtonPalette.Mode, IsManualMode, _isManualPressed, isHovered: false, IsManualCommandEnabled, foreground: true);
    public IBrush ResetBackground => CommandBrush(RouteMapCommandButtonPalette.Reset, IsResetActive, _isResetPressed, _isResetHovered, IsResetCommandEnabled, foreground: false);
    public IBrush ResetForeground => CommandBrush(RouteMapCommandButtonPalette.Reset, IsResetActive, _isResetPressed, _isResetHovered, IsResetCommandEnabled, foreground: true);
    public IBrush EmergencyBackground => CommandBrush(RouteMapCommandButtonPalette.Emergency, HasEmergency, _isEmergencyPressed, _isEmergencyHovered, IsEmergencyCommandEnabled, foreground: false);
    public IBrush EmergencyForeground => CommandBrush(RouteMapCommandButtonPalette.Emergency, HasEmergency, _isEmergencyPressed, _isEmergencyHovered, IsEmergencyCommandEnabled, foreground: true);

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SwitchToAutomaticCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SwitchToManualCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> ResetCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> EmergencyCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenSettingsCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenHelpCommand { get; }

    public void ApplySettings(RouteTopBarSettings? settings, RouteMapPaletteSettings? palette = null)
    {
        _settings = settings ?? CreateDefaultSettings();
        if (palette is not null)
            _disabledColor = palette.Disabled;
        RaiseButtonProperties();
    }

    public void ApplyRuntime(
        bool isAutomaticMode,
        bool isManualMode,
        bool isResetActive,
        bool hasEmergency,
        bool isConnectionAvailable,
        bool isAutomaticCommandEnabled = true,
        bool isManualCommandEnabled = true,
        bool isResetCommandEnabled = true,
        bool isEmergencyCommandEnabled = true)
    {
        IsAutomaticMode = isAutomaticMode;
        IsManualMode = isManualMode;
        IsResetActive = isResetActive;
        HasEmergency = hasEmergency;
        IsAutomaticCommandEnabled = isConnectionAvailable && isAutomaticCommandEnabled;
        IsManualCommandEnabled = isConnectionAvailable && isManualCommandEnabled;
        IsResetCommandEnabled = isConnectionAvailable && isResetCommandEnabled;
        IsEmergencyCommandEnabled = isConnectionAvailable && isEmergencyCommandEnabled;
        this.RaisePropertyChanged(nameof(AreCommandsEnabled));
        RaiseButtonProperties();
    }

    public void SetAutomaticPressed(bool isPressed)
    {
        if (_isAutomaticPressed == isPressed)
            return;

        _isAutomaticPressed = isPressed;
        this.RaisePropertyChanged(nameof(AutomaticBackground));
        this.RaisePropertyChanged(nameof(AutomaticForeground));
    }

    public void SetManualPressed(bool isPressed)
    {
        if (_isManualPressed == isPressed)
            return;

        _isManualPressed = isPressed;
        this.RaisePropertyChanged(nameof(ManualBackground));
        this.RaisePropertyChanged(nameof(ManualForeground));
    }

    public void SetEmergencyPressed(bool isPressed)
    {
        if (_isEmergencyPressed == isPressed)
            return;

        _isEmergencyPressed = isPressed;
        this.RaisePropertyChanged(nameof(EmergencyBackground));
        this.RaisePropertyChanged(nameof(EmergencyForeground));
    }

    public void SetResetPressed(bool isPressed)
    {
        if (_isResetPressed == isPressed)
            return;

        _isResetPressed = isPressed;
        this.RaisePropertyChanged(nameof(ResetBackground));
        this.RaisePropertyChanged(nameof(ResetForeground));
    }

    public void SetResetHovered(bool isHovered)
    {
        if (_isResetHovered == isHovered)
            return;

        _isResetHovered = isHovered;
        this.RaisePropertyChanged(nameof(ResetBackground));
        this.RaisePropertyChanged(nameof(ResetForeground));
    }

    public void SetEmergencyHovered(bool isHovered)
    {
        if (_isEmergencyHovered == isHovered)
            return;

        _isEmergencyHovered = isHovered;
        this.RaisePropertyChanged(nameof(EmergencyBackground));
        this.RaisePropertyChanged(nameof(EmergencyForeground));
    }

    private async Task SwitchToAutomaticAsync()
    {
        if (!IsAutomaticCommandEnabled)
            return;

        IsAutomaticMode = true;
        IsManualMode = false;
        RaiseButtonProperties();
        await DispatchAsync(_settings.Manual.Binding, false);
        await DispatchAsync(_settings.Automatic.Binding, true);
    }

    private async Task SwitchToManualAsync()
    {
        if (!IsManualCommandEnabled)
            return;

        IsAutomaticMode = false;
        IsManualMode = true;
        RaiseButtonProperties();
        await DispatchAsync(_settings.Automatic.Binding, false);
        await DispatchAsync(_settings.Manual.Binding, true);
    }

    private async Task ExecuteEmergencyAsync()
    {
        if (!IsEmergencyCommandEnabled)
            return;

        HasEmergency = !HasEmergency;
        RaiseButtonProperties();
        await DispatchAsync(_settings.Emergency.Binding, HasEmergency);
    }

    private async Task ExecuteResetAsync()
    {
        if (!IsResetCommandEnabled)
            return;

        IsResetActive = true;
        RaiseButtonProperties();
        try
        {
            await DispatchAsync(_settings.Reset.Binding, true);
        }
        finally
        {
            IsResetActive = false;
            RaiseButtonProperties();
        }
    }

    private Task DispatchAsync(SignalBinding binding, bool value)
    {
        if (_commandDispatcher is null || binding.Direction == SignalBindingDirection.Read)
            return Task.CompletedTask;
        return _commandDispatcher.DispatchAsync(new SignalWriteRequest(binding.SignalId, value, binding.ValueType));
    }

    private void RaiseButtonProperties()
    {
        this.RaisePropertyChanged(nameof(AutomaticText));
        this.RaisePropertyChanged(nameof(ManualText));
        this.RaisePropertyChanged(nameof(ResetText));
        this.RaisePropertyChanged(nameof(EmergencyText));
        this.RaisePropertyChanged(nameof(AutomaticBackground));
        this.RaisePropertyChanged(nameof(AutomaticForeground));
        this.RaisePropertyChanged(nameof(ManualBackground));
        this.RaisePropertyChanged(nameof(ManualForeground));
        this.RaisePropertyChanged(nameof(ResetBackground));
        this.RaisePropertyChanged(nameof(ResetForeground));
        this.RaisePropertyChanged(nameof(EmergencyBackground));
        this.RaisePropertyChanged(nameof(EmergencyForeground));
    }

    private static IBrush ButtonBrush(
        RouteMapCommandButtonColors colors,
        bool isChecked,
        bool isPressed,
        bool isHovered,
        bool foreground) =>
        RouteMapPalette.Brush(foreground
            ? colors.Foreground(isPressed, isChecked)
            : colors.Background(isPressed, isChecked, isHovered));

    private IBrush CommandBrush(
        RouteMapCommandButtonColors colors,
        bool isChecked,
        bool isPressed,
        bool isHovered,
        bool isEnabled,
        bool foreground) =>
        isEnabled
            ? ButtonBrush(colors, isChecked, isPressed, isHovered, foreground)
            : RouteMapPalette.Brush(foreground ? "#FFFFFF" : _disabledColor);

    private static RouteTopBarSettings CreateDefaultSettings() =>
        RouteMapSeed.Create().TopBar ?? throw new InvalidOperationException("RouteMap seed does not define TopBar settings.");
}
