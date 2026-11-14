using Avalonia.Media;
using Avalonia.Threading;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Dialogs.HelpDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using ReactiveUI;
using System.Reactive;
using System.Reactive.Linq;

namespace Configurator.Desktop.Workspace.RouteMap.ViewModels;

public sealed class TopBarViewModel : ViewModelBase
{
    private readonly IEquipmentCommandDispatcher? _commandDispatcher;
    private readonly IHelpDialogService? _helpDialogService;
    private readonly Action<string>? _commandErrorHandler;
    private readonly Action? _commandSuccessHandler;
    private readonly bool _isAdminMode;
    private readonly Func<SignalBinding, string?>? _getMappingError;
    private readonly Func<SignalWriteRequest, CancellationToken, Task<bool>>? _tryDispatchAdminCommand;
    private readonly AdminCommandState _unload = new(SignalBindingRole.UnloadCommand);
    private readonly AdminCommandState _rotation = new(SignalBindingRole.RotationCommand);
    private readonly AdminCommandState _load = new(SignalBindingRole.LoadCommand);
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
    private bool _isAutomaticCommandEnabled;
    private bool _isManualCommandEnabled;
    private bool _isResetCommandEnabled = true;
    private bool _isEmergencyCommandEnabled = true;
    private bool _lastEmergencyReadback;

    public TopBarViewModel(
        IRouteMapSettingsDialogService? settingsDialogService = null,
        IEquipmentCommandDispatcher? commandDispatcher = null,
        RouteTopBarSettings? settings = null,
        bool isSettingsVisible = true,
        RouteMapPaletteSettings? palette = null,
        IHelpDialogService? helpDialogService = null,
        Action<string>? commandErrorHandler = null,
        Action? commandSuccessHandler = null)
        : this(false, settingsDialogService, commandDispatcher, settings, isSettingsVisible, palette,
            helpDialogService, commandErrorHandler, commandSuccessHandler)
    {
    }

    internal TopBarViewModel(
        bool isAdminMode,
        IRouteMapSettingsDialogService? settingsDialogService = null,
        IEquipmentCommandDispatcher? commandDispatcher = null,
        RouteTopBarSettings? settings = null,
        bool isSettingsVisible = true,
        RouteMapPaletteSettings? palette = null,
        IHelpDialogService? helpDialogService = null,
        Action<string>? commandErrorHandler = null,
        Action? commandSuccessHandler = null,
        Func<SignalBinding, string?>? getMappingError = null,
        Func<SignalWriteRequest, CancellationToken, Task<bool>>? tryDispatchAdminCommand = null)
    {
        _commandDispatcher = commandDispatcher;
        _helpDialogService = helpDialogService;
        _commandErrorHandler = commandErrorHandler;
        _commandSuccessHandler = commandSuccessHandler;
        _isAdminMode = isAdminMode;
        _getMappingError = getMappingError;
        _tryDispatchAdminCommand = tryDispatchAdminCommand;
        _settings = settings ?? CreateDefaultSettings();
        _disabledColor = (palette ?? new RouteMapPaletteSettings()).Disabled;
        IsSettingsVisible = isSettingsVisible;
        ApplyAdminCommandSettings(settings);

        // Retain the public commands for existing callers; legacy modes are inert.
        SwitchToAutomaticCommand = ReactiveCommand.Create(() => { });
        SwitchToManualCommand = ReactiveCommand.Create(() => { });
        ResetCommand = ReactiveCommand.CreateFromTask(ExecuteResetAsync);
        EmergencyCommand = ReactiveCommand.CreateFromTask(ExecuteEmergencyAsync);
        UnloadCommand = CreateAdminCommand(_unload, nameof(IsUnloadEnabled));
        RotationCommand = CreateAdminCommand(_rotation, nameof(IsRotationEnabled));
        LoadCommand = CreateAdminCommand(_load, nameof(IsLoadEnabled));
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
        IsResetCommandEnabled && IsEmergencyCommandEnabled;

    public bool IsSettingsVisible { get; }
    public bool AreAdminCommandsVisible => _isAdminMode;

    public string UnloadText => _unload.Settings?.Text ?? string.Empty;
    public string RotationText => _rotation.Settings?.Text ?? string.Empty;
    public string LoadText => _load.Settings?.Text ?? string.Empty;
    public bool IsUnloadEnabled => CanExecuteAdminCommand(_unload);
    public bool IsRotationEnabled => CanExecuteAdminCommand(_rotation);
    public bool IsLoadEnabled => CanExecuteAdminCommand(_load);
    public bool IsUnloadSending => _unload.IsSending;
    public bool IsRotationSending => _rotation.IsSending;
    public bool IsLoadSending => _load.IsSending;
    public bool? UnloadEnabledSignalValue => _unload.Runtime.EnabledSignalValue;
    public bool? RotationEnabledSignalValue => _rotation.Runtime.EnabledSignalValue;
    public bool? LoadEnabledSignalValue => _load.Runtime.EnabledSignalValue;
    public string? UnloadMappingError => _unload.MappingError;
    public string? RotationMappingError => _rotation.MappingError;
    public string? LoadMappingError => _load.MappingError;

    public ReactiveCommand<Unit, Unit> UnloadCommand { get; }
    public ReactiveCommand<Unit, Unit> RotationCommand { get; }
    public ReactiveCommand<Unit, Unit> LoadCommand { get; }

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
        ApplyAdminCommandSettings(settings);
        if (palette is not null)
            _disabledColor = palette.Disabled;
        RaiseButtonProperties();
        this.RaisePropertyChanged(nameof(UnloadText));
        this.RaisePropertyChanged(nameof(RotationText));
        this.RaisePropertyChanged(nameof(LoadText));
        RaiseAdminCommandRuntimeProperties();
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
        _lastEmergencyReadback = hasEmergency;
        IsAutomaticMode = isAutomaticMode;
        IsManualMode = isManualMode;
        IsResetActive = isResetActive;
        HasEmergency = hasEmergency;
        IsAutomaticCommandEnabled = false;
        IsManualCommandEnabled = false;
        IsResetCommandEnabled = isConnectionAvailable && isResetCommandEnabled;
        IsEmergencyCommandEnabled = isConnectionAvailable && isEmergencyCommandEnabled;
        this.RaisePropertyChanged(nameof(AreCommandsEnabled));
        RaiseButtonProperties();
    }

    internal void ApplyAdminCommandRuntime(RouteMapRuntimeState runtime)
    {
        var unloadChanged = ApplyAdminCommandRuntime(_unload, runtime.UnloadCommand);
        var rotationChanged = ApplyAdminCommandRuntime(_rotation, runtime.RotationCommand);
        var loadChanged = ApplyAdminCommandRuntime(_load, runtime.LoadCommand);
        if (unloadChanged || rotationChanged || loadChanged)
            RaiseAdminCommandRuntimeProperties();
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

    private async Task ExecuteEmergencyAsync()
    {
        if (!IsEmergencyCommandEnabled)
            return;

        await ExecuteCommandAsync(
            EmergencyText,
            async () =>
            {
                HasEmergency = !HasEmergency;
                RaiseButtonProperties();
                await DispatchAsync(_settings.Emergency.Binding, HasEmergency);
            },
            RestoreEmergencyReadback);
    }

    private async Task ExecuteResetAsync()
    {
        if (!IsResetCommandEnabled)
            return;

        await ExecuteCommandAsync(
            ResetText,
            async () =>
            {
                IsResetActive = true;
                RaiseButtonProperties();
                try
                {
                    await DispatchAsync(_settings.Reset.Binding, true, ModbusWriteMode.Pulse);
                }
                finally
                {
                    IsResetActive = false;
                    RaiseButtonProperties();
                }
            });
    }

    private ReactiveCommand<Unit, Unit> CreateAdminCommand(AdminCommandState command, string enabledProperty) =>
        ReactiveCommand.CreateFromObservable(
            () => Observable.FromAsync(cancellationToken => ExecuteAdminCommandOnUiAsync(command, cancellationToken)),
            this.Changed.Where(change => change.PropertyName == enabledProperty)
                .Select(_ => CanExecuteAdminCommand(command))
                .StartWith(CanExecuteAdminCommand(command))
                .DistinctUntilChanged());

    private bool CanExecuteAdminCommand(AdminCommandState command) =>
        _isAdminMode && _commandDispatcher is not null && !command.IsSending &&
        command.Runtime.CanExecute && command.MappingError is null &&
        command.Settings?.GetMomentaryCommandBinding(command.Role) is not null;

    private Task ExecuteAdminCommandOnUiAsync(AdminCommandState command, CancellationToken cancellationToken)
    {
        if (!_isAdminMode || command.IsSending || cancellationToken.IsCancellationRequested)
            return Task.CompletedTask;

        return Avalonia.Application.Current is null || Dispatcher.UIThread.CheckAccess()
            ? ExecuteAdminCommandAsync(command, cancellationToken)
            : Dispatcher.UIThread.InvokeAsync(() => ExecuteAdminCommandAsync(command, cancellationToken));
    }

    private async Task ExecuteAdminCommandAsync(AdminCommandState command, CancellationToken cancellationToken)
    {
        var settings = command.Settings;
        if (!_isAdminMode || _commandDispatcher is null || !command.Runtime.CanExecute ||
            settings is null || settings.GetMomentaryCommandBinding(command.Role) is not { } binding ||
            cancellationToken.IsCancellationRequested || !command.TryBeginSending())
            return;

        try
        {
            RaiseAdminCommandSendingProperties();

            if (!command.Runtime.CanExecute || !ReferenceEquals(command.Settings, settings) || cancellationToken.IsCancellationRequested)
                return;

            var mappingError = _getMappingError?.Invoke(binding);
            if (mappingError is not null)
            {
                if (command.MappingError != mappingError)
                {
                    command.MappingError = mappingError;
                    RaiseAdminCommandRuntimeProperties();
                }
                _commandErrorHandler?.Invoke($"Команда «{settings.Text}» недоступна: {mappingError}");
                return;
            }

            var request = new SignalWriteRequest(binding.SignalId, true, binding.ValueType)
            {
                RequiredWriteMode = ModbusWriteMode.Pulse,
            };
            if (_tryDispatchAdminCommand is null)
                await _commandDispatcher.DispatchAsync(request, cancellationToken);
            else if (!await _tryDispatchAdminCommand(request, cancellationToken))
                return;

            _commandSuccessHandler?.Invoke();
        }
        catch (OperationCanceledException)
        {
            _commandErrorHandler?.Invoke($"Отправка команды «{settings.Text}» отменена.");
        }
        catch (TimeoutException ex)
        {
            _commandErrorHandler?.Invoke($"Истекло время ожидания команды «{settings.Text}»: {ex.Message}");
        }
        catch (Exception ex)
        {
            _commandErrorHandler?.Invoke($"Ошибка отправки команды «{settings.Text}»: {ex.Message}");
        }
        finally
        {
            command.EndSending();
            RaiseAdminCommandSendingProperties();
        }
    }

    private void ApplyAdminCommandSettings(RouteTopBarSettings? settings)
    {
        ApplyAdminCommandSettings(_unload, settings?.Unload);
        ApplyAdminCommandSettings(_rotation, settings?.Rotation);
        ApplyAdminCommandSettings(_load, settings?.Load);
    }

    private void ApplyAdminCommandSettings(AdminCommandState command, RouteTopBarButtonSettings? settings)
    {
        if (command.Settings?.Binding != settings?.Binding || command.Settings?.EnabledBinding != settings?.EnabledBinding)
            command.Runtime = default;

        command.Settings = settings;
        command.MappingError = GetAdminCommandMappingError(command);
    }

    private bool ApplyAdminCommandRuntime(AdminCommandState command, TopBarCommandRuntimeState runtime)
    {
        var error = GetAdminCommandMappingError(command);
        var changed = command.Runtime != runtime || command.MappingError != error;
        command.Runtime = runtime;
        command.MappingError = error;
        return changed;
    }

    private string? GetAdminCommandMappingError(AdminCommandState command) =>
        _isAdminMode && command.Settings?.GetMomentaryCommandBinding(command.Role) is { } binding
            ? _getMappingError?.Invoke(binding)
            : null;

    private void RaiseAdminCommandRuntimeProperties()
    {
        this.RaisePropertyChanged(nameof(UnloadEnabledSignalValue));
        this.RaisePropertyChanged(nameof(RotationEnabledSignalValue));
        this.RaisePropertyChanged(nameof(LoadEnabledSignalValue));
        this.RaisePropertyChanged(nameof(UnloadMappingError));
        this.RaisePropertyChanged(nameof(RotationMappingError));
        this.RaisePropertyChanged(nameof(LoadMappingError));
        RaiseAdminCommandEnabledProperties();
    }

    private void RaiseAdminCommandSendingProperties()
    {
        this.RaisePropertyChanged(nameof(IsUnloadSending));
        this.RaisePropertyChanged(nameof(IsRotationSending));
        this.RaisePropertyChanged(nameof(IsLoadSending));
        RaiseAdminCommandEnabledProperties();
    }

    private void RaiseAdminCommandEnabledProperties()
    {
        this.RaisePropertyChanged(nameof(IsUnloadEnabled));
        this.RaisePropertyChanged(nameof(IsRotationEnabled));
        this.RaisePropertyChanged(nameof(IsLoadEnabled));
    }

    private async Task ExecuteCommandAsync(
        string commandText,
        Func<Task> executeAsync,
        Action? restoreReadback = null)
    {
        try
        {
            await executeAsync();
        }
        catch (OperationCanceledException)
        {
            restoreReadback?.Invoke();
            return;
        }
        catch (Exception ex)
        {
            restoreReadback?.Invoke();
            _commandErrorHandler?.Invoke($"Команда «{commandText}» не отправлена: {ex.Message}");
            return;
        }

        _commandSuccessHandler?.Invoke();
    }

    private void RestoreEmergencyReadback()
    {
        HasEmergency = _lastEmergencyReadback;
        RaiseButtonProperties();
    }

    private Task DispatchAsync(SignalBinding binding, bool value, ModbusWriteMode? requiredWriteMode = null)
    {
        if (_commandDispatcher is null || binding.Direction == SignalBindingDirection.Read)
            return Task.CompletedTask;
        return _commandDispatcher.DispatchAsync(new SignalWriteRequest(binding.SignalId, value, binding.ValueType)
        {
            RequiredWriteMode = requiredWriteMode,
        });
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

    private sealed class AdminCommandState(SignalBindingRole role)
    {
        private int _sending;
        public SignalBindingRole Role { get; } = role;
        public RouteTopBarButtonSettings? Settings { get; set; }
        public TopBarCommandRuntimeState Runtime { get; set; }
        public string? MappingError { get; set; }
        public bool IsSending => Volatile.Read(ref _sending) != 0;

        public bool TryBeginSending() => Interlocked.CompareExchange(ref _sending, 1, 0) == 0;
        public void EndSending() => Volatile.Write(ref _sending, 0);
    }
}
