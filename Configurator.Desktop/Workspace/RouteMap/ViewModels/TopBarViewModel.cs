using Avalonia.Media;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using ReactiveUI;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class TopBarViewModel : ViewModelBase, IDisposable
{
    private readonly IEquipmentCommandDispatcher? _commandDispatcher;
    private readonly IRouteMapSettingsDialogService? _settingsDialogService;
    private readonly CompositeDisposable _disposables = new();
    private readonly BehaviorSubject<bool> _canOpenSettingsChanged;
    private RouteTopBarSettings _settings;
    private bool _isAutomaticMode;
    private bool _isManualMode = true;
    private bool _hasEmergency;
    private bool _isAutomaticPressed;
    private bool _isManualPressed;
    private bool _isEmergencyPressed;
    private bool _canOpenSettings;
    private string? _settingsErrorMessage;
    private string _connectionStatusText = "Ожидание";

    public TopBarViewModel(
        IRouteMapSettingsDialogService? settingsDialogService = null,
        IEquipmentCommandDispatcher? commandDispatcher = null,
        RouteTopBarSettings? settings = null,
        bool canOpenSettings = true)
    {
        _settingsDialogService = settingsDialogService;
        _commandDispatcher = commandDispatcher;
        _settings = settings ?? CreateDefaultSettings();
        _canOpenSettings = canOpenSettings;
        _canOpenSettingsChanged = new BehaviorSubject<bool>(canOpenSettings);

        SwitchToAutomaticCommand = ReactiveCommand.CreateFromTask(SwitchToAutomaticAsync);
        SwitchToManualCommand = ReactiveCommand.CreateFromTask(SwitchToManualAsync);
        EmergencyCommand = ReactiveCommand.CreateFromTask(ExecuteEmergencyAsync);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(
            OpenSettingsAsync,
            _canOpenSettingsChanged);
        _disposables.Add(_canOpenSettingsChanged);
        _disposables.Add(OpenSettingsCommand.ThrownExceptions.Subscribe(ex =>
            SettingsErrorMessage = $"RouteMap settings failed: {ex.Message}"));
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

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => this.RaiseAndSetIfChanged(ref _connectionStatusText, value);
    }

    public bool CanOpenSettings
    {
        get => _canOpenSettings;
        private set
        {
            this.RaiseAndSetIfChanged(ref _canOpenSettings, value);
            _canOpenSettingsChanged.OnNext(value);
        }
    }

    public string? SettingsErrorMessage
    {
        get => _settingsErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _settingsErrorMessage, value);
    }

    public string AutomaticText => _settings.Automatic.Text;
    public string ManualText => _settings.Manual.Text;
    public string EmergencyText => _settings.Emergency.Text;
    public IBrush AutomaticBackground => ButtonBrush(_settings.Automatic, IsAutomaticMode, _isAutomaticPressed, foreground: false);
    public IBrush AutomaticForeground => ButtonBrush(_settings.Automatic, IsAutomaticMode, _isAutomaticPressed, foreground: true);
    public IBrush ManualBackground => ButtonBrush(_settings.Manual, IsManualMode, _isManualPressed, foreground: false);
    public IBrush ManualForeground => ButtonBrush(_settings.Manual, IsManualMode, _isManualPressed, foreground: true);
    public IBrush EmergencyBackground => ButtonBrush(_settings.Emergency, HasEmergency, _isEmergencyPressed, foreground: false);
    public IBrush EmergencyForeground => ButtonBrush(_settings.Emergency, HasEmergency, _isEmergencyPressed, foreground: true);

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SwitchToAutomaticCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SwitchToManualCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> EmergencyCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenSettingsCommand { get; }

    public void SetCanOpenSettings(bool canOpenSettings) => CanOpenSettings = canOpenSettings;

    public void Dispose() => _disposables.Dispose();

    public void ApplySettings(RouteTopBarSettings? settings)
    {
        _settings = settings ?? CreateDefaultSettings();
        RaiseButtonProperties();
    }

    public void ApplyRuntime(bool isAutomaticMode, bool isManualMode, bool hasEmergency, string connectionStatusText)
    {
        IsAutomaticMode = isAutomaticMode;
        IsManualMode = isManualMode;
        HasEmergency = hasEmergency;
        ConnectionStatusText = connectionStatusText;
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

    private async Task SwitchToAutomaticAsync()
    {
        IsAutomaticMode = true;
        IsManualMode = false;
        RaiseButtonProperties();
        await DispatchAsync(_settings.Manual.Binding, false);
        await DispatchAsync(_settings.Automatic.Binding, true);
    }

    private async Task SwitchToManualAsync()
    {
        IsAutomaticMode = false;
        IsManualMode = true;
        RaiseButtonProperties();
        await DispatchAsync(_settings.Automatic.Binding, false);
        await DispatchAsync(_settings.Manual.Binding, true);
    }

    private async Task ExecuteEmergencyAsync()
    {
        HasEmergency = !HasEmergency;
        RaiseButtonProperties();
        await DispatchAsync(_settings.Emergency.Binding, HasEmergency);
    }

    private async Task OpenSettingsAsync()
    {
        SettingsErrorMessage = null;
        if (_settingsDialogService is null)
        {
            return;
        }

        await _settingsDialogService.ShowAsync();
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
        this.RaisePropertyChanged(nameof(EmergencyText));
        this.RaisePropertyChanged(nameof(AutomaticBackground));
        this.RaisePropertyChanged(nameof(AutomaticForeground));
        this.RaisePropertyChanged(nameof(ManualBackground));
        this.RaisePropertyChanged(nameof(ManualForeground));
        this.RaisePropertyChanged(nameof(EmergencyBackground));
        this.RaisePropertyChanged(nameof(EmergencyForeground));
    }

    private static IBrush ButtonBrush(RouteTopBarButtonSettings button, bool isChecked, bool isPressed, bool foreground) =>
        RouteMapPalette.Brush(foreground
            ? isPressed ? button.PressedForeground : isChecked ? button.CheckedForeground : button.NormalForeground
            : isPressed ? button.PressedBackground : isChecked ? button.CheckedBackground : button.NormalBackground);

    private static RouteTopBarSettings CreateDefaultSettings() =>
        RouteMapSeed.Create().TopBar ?? throw new InvalidOperationException("RouteMap seed does not define TopBar settings.");
}
