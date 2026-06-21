using Avalonia.Media;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using ReactiveUI;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class TopBarViewModel : ViewModelBase
{
    private readonly IEquipmentCommandDispatcher? _commandDispatcher;
    private RouteTopBarSettings _settings;
    private bool _isAutomaticMode;
    private bool _isManualMode = true;
    private bool _hasEmergency;
    private string _connectionStatusText = "Ожидание";

    public TopBarViewModel(
        IRouteMapSettingsDialogService? settingsDialogService = null,
        IEquipmentCommandDispatcher? commandDispatcher = null,
        RouteTopBarSettings? settings = null)
    {
        _commandDispatcher = commandDispatcher;
        _settings = settings ?? CreateDefaultSettings();

        SwitchToAutomaticCommand = ReactiveCommand.CreateFromTask(SwitchToAutomaticAsync);
        SwitchToManualCommand = ReactiveCommand.CreateFromTask(SwitchToManualAsync);
        EmergencyCommand = ReactiveCommand.CreateFromTask(ExecuteEmergencyAsync);
        OpenSettingsCommand = ReactiveCommand.CreateFromTask(() =>
            settingsDialogService?.ShowAsync() ?? Task.CompletedTask);
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

    public string AutomaticText => _settings.Automatic.Text;
    public string ManualText => _settings.Manual.Text;
    public string EmergencyText => _settings.Emergency.Text;
    public IBrush AutomaticBackground => ButtonBrush(_settings.Automatic, IsAutomaticMode, foreground: false);
    public IBrush AutomaticForeground => ButtonBrush(_settings.Automatic, IsAutomaticMode, foreground: true);
    public IBrush ManualBackground => ButtonBrush(_settings.Manual, IsManualMode, foreground: false);
    public IBrush ManualForeground => ButtonBrush(_settings.Manual, IsManualMode, foreground: true);
    public IBrush EmergencyBackground => ButtonBrush(_settings.Emergency, HasEmergency, foreground: false);
    public IBrush EmergencyForeground => ButtonBrush(_settings.Emergency, HasEmergency, foreground: true);

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SwitchToAutomaticCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SwitchToManualCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> EmergencyCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> OpenSettingsCommand { get; }

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

    private static IBrush ButtonBrush(RouteTopBarButtonSettings button, bool isChecked, bool foreground) =>
        RouteMapPalette.Brush(foreground
            ? isChecked ? button.CheckedForeground : button.NormalForeground
            : isChecked ? button.CheckedBackground : button.NormalBackground);

    private static RouteTopBarSettings CreateDefaultSettings() =>
        RouteMapSeed.Create().TopBar ?? throw new InvalidOperationException("RouteMap seed does not define TopBar settings.");
}
