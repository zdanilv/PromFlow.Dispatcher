using System.Globalization;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapRuntimeMapper : IRouteMapRuntimeMapper<RouteMapRuntimeState>
{
    private static readonly IReadOnlyDictionary<string, SignalValue> EmptySignals = new Dictionary<string, SignalValue>();
    private readonly RouteMapConfigurationManager? _configurationManager;
    private readonly RouteMapDefinition? _fixedDefinition;

    public RouteMapRuntimeMapper(RouteMapConfigurationManager configurationManager)
    {
        _configurationManager = configurationManager;
    }

    public RouteMapRuntimeMapper(RouteMapDefinition definition)
    {
        _fixedDefinition = definition;
    }

    public RouteMapRuntimeState Map(IReadOnlyDictionary<string, SignalValue>? signals)
        => Map(signals, _configurationManager?.CurrentDefinition ?? _fixedDefinition
            ?? throw new InvalidOperationException("RouteMap definition is unavailable."));

    internal RouteMapRuntimeState Map(IReadOnlyDictionary<string, SignalValue>? signals, RouteMapDefinition definition)
    {
        var hasSnapshot = signals is not null;
        signals ??= EmptySignals;
        var objects = new Dictionary<string, RouteObjectRuntimeState>();
        var isConnectionAvailable = hasSnapshot && ReadConnectionAvailable(signals);
        var isAdminCommandConnectionAvailable = ReadGoodBool(signals, RouteMapSystemSignalIds.ConnectionConnected) == true;
        var isGlobalFaultActive = ReadBool(signals, RouteMapSystemSignalIds.GlobalFault);
        var palette = definition.Display?.Palette ?? new RouteMapPaletteSettings();

        foreach (var node in definition.Nodes)
            objects[node.Id] = MapObject(
                node.Id,
                node.State,
                node.Bindings,
                signals,
                canStartFallback: false,
                canStopFallback: false,
                activeAsState: false,
                forceOffline: !isConnectionAvailable,
                forceFault: isGlobalFaultActive);

        foreach (var segment in definition.Segments)
            objects[segment.Id] = MapObject(
                segment.Id,
                segment.State,
                segment.Bindings,
                signals,
                canStartFallback: false,
                canStopFallback: false,
                activeAsState: true,
                activeFragments: segment.ActiveFragments,
                forceOffline: !isConnectionAvailable,
                forceFault: isGlobalFaultActive);

        foreach (var vehicle in definition.Vehicles)
            objects[vehicle.Id] = MapObject(
                vehicle.Id,
                vehicle.State,
                vehicle.Bindings,
                signals,
                canStartFallback: false,
                canStopFallback: false,
                forceOffline: !isConnectionAvailable,
                forceFault: isGlobalFaultActive);

        foreach (var equipment in definition.MapEquipment)
        {
            objects[equipment.Id] = MapObject(
                equipment.Id,
                equipment.State,
                equipment.Bindings,
                signals,
                equipment.CanStart,
                equipment.CanStop,
                equipment.StatusText,
                forceOffline: !isConnectionAvailable,
                forceFault: isGlobalFaultActive,
                forceCommandsDisabled: !isConnectionAvailable,
                offlineText: "Не в сети",
                statusMapping: equipment.StatusMapping,
                statusPalette: palette);
        }

        return new RouteMapRuntimeState(
            objects,
            IsAutomaticMode: ReadTopBarBool(signals, definition.TopBar?.Automatic, fallback: false),
            IsManualMode: ReadTopBarBool(signals, definition.TopBar?.Manual, fallback: true),
            IsResetActive: ReadTopBarBool(signals, definition.TopBar?.Reset, fallback: false),
            HasEmergency: ReadTopBarBool(signals, definition.TopBar?.Emergency, fallback: false),
            IsQueueRunning: ReadBool(signals, RouteMapSystemSignalIds.QueueRunning),
            ConnectionStatusText: ReadString(signals, RouteMapSystemSignalIds.ConnectionStatus) ?? "Ожидание",
            IsConnectionAvailable: isConnectionAvailable,
            IsAutomaticCommandEnabled: ReadTopBarEnabled(signals, definition.TopBar?.Automatic, isConnectionAvailable),
            IsManualCommandEnabled: ReadTopBarEnabled(signals, definition.TopBar?.Manual, isConnectionAvailable),
            IsResetCommandEnabled: ReadTopBarEnabled(signals, definition.TopBar?.Reset, isConnectionAvailable),
            IsEmergencyCommandEnabled: ReadTopBarEnabled(signals, definition.TopBar?.Emergency, isConnectionAvailable))
        {
            UnloadCommand = MapTopBarCommand(signals, definition.TopBar?.Unload,
                SignalBindingRole.UnloadCommand, isAdminCommandConnectionAvailable),
            RotationCommand = MapTopBarCommand(signals, definition.TopBar?.Rotation,
                SignalBindingRole.RotationCommand, isAdminCommandConnectionAvailable),
            LoadCommand = MapTopBarCommand(signals, definition.TopBar?.Load,
                SignalBindingRole.LoadCommand, isAdminCommandConnectionAvailable),
            FrequencyValueText = isConnectionAvailable ? ReadFrequency(signals, definition.Display?.FrequencyBinding) : null,
        };
    }

    private static string? ReadFrequency(IReadOnlyDictionary<string, SignalValue> signals, SignalBinding? binding)
    {
        if (binding is null || !signals.TryGetValue(binding.SignalId, out var signal) ||
            !signal.IsQualityGood || signal.IsStale ||
            !SignalModbusTypeCompatibility.IsCompatible(binding.ValueType, SignalModbusTypeCompatibility.DefaultModbusType(signal.ValueType)))
            return null;

        IFormattable? number = signal switch
        {
            { ValueType: SignalValueType.UInt16 or SignalValueType.Word, Value: ushort value } => value,
            { ValueType: SignalValueType.Int16, Value: short value } => value,
            { ValueType: SignalValueType.Int32, Value: int value } => value,
            { ValueType: SignalValueType.Dword, Value: uint value } => value,
            { ValueType: SignalValueType.Float32, Value: float value } when float.IsFinite(value) => value,
            _ => null,
        };

        return number?.ToString(null, CultureInfo.CurrentCulture);
    }

    private static RouteObjectRuntimeState MapObject(
        string objectId,
        RouteObjectState fallbackState,
        IReadOnlyList<SignalBinding> bindings,
        IReadOnlyDictionary<string, SignalValue> signals,
        bool canStartFallback,
        bool canStopFallback,
        string? textFallback = null,
        bool activeAsState = true,
        IReadOnlyList<RouteSegmentActiveFragment>? activeFragments = null,
        bool forceOffline = false,
        bool forceFault = false,
        bool forceCommandsDisabled = false,
        string? offlineText = null,
        EquipmentStatusMapping? statusMapping = null,
        RouteMapPaletteSettings? statusPalette = null)
    {
        var text = textFallback;
        string? valueText = null;
        var isVisible = true;
        var isStartChecked = false;
        var isStopChecked = false;
        bool? stopCommandReadback = null;
        var isStartOff = false;
        var isStopOff = false;
        var hasPowerFeedbackBinding = bindings.Any(binding => binding.Role == SignalBindingRole.PowerFeedback);
        var isUncheckedSelector = false;
        var isCheckedSelector = false;
        bool? isPowerOn = null;
        var isEnabled = true;
        var hasGoodDisabledSignal = false;
        var isOffline = false;
        var hasFault = false;
        var isActiveRoute = false;
        var hasActiveSignal = false;
        bool? isLoader = null;
        bool? isTarget = null;
        var activeFragmentIndexes = new HashSet<int>();
        SignalBinding? statusBinding = null;
        SignalValue? statusSignal = null;

        foreach (var binding in bindings)
        {
            if (IsIgnoredRuntimeRole(binding.Role))
                continue;

            if (statusMapping is not null && binding.Role == SignalBindingRole.Text)
            {
                statusBinding = binding;
                signals.TryGetValue(binding.SignalId, out statusSignal);
                continue;
            }

            if (!signals.TryGetValue(binding.SignalId, out var signal))
                continue;

            if (!signal.IsQualityGood || signal.IsStale)
            {
                isOffline = true;
                continue;
            }

            switch (binding.Role)
            {
                case SignalBindingRole.State:
                    break;
                case SignalBindingRole.Text:
                    text = ReadText(signal, text);
                    break;
                case SignalBindingRole.Value:
                    valueText = signal.Value?.ToString();
                    break;
                case SignalBindingRole.Visible:
                    isVisible = signal.Value is bool visible && visible;
                    break;
                case SignalBindingRole.Enabled:
                    if (signal.Value is bool enabled)
                    {
                        isEnabled = enabled;
                        hasGoodDisabledSignal = !enabled;
                    }
                    else
                    {
                        isEnabled = true;
                    }
                    break;
                case SignalBindingRole.Fault:
                    if (signal.Value is bool isFault && isFault)
                        hasFault = true;
                    break;
                case SignalBindingRole.ActiveRoute:
                    if (signal.Value is bool isActive && isActive)
                    {
                        isActiveRoute = true;
                        hasActiveSignal = true;
                    }
                    break;
                case SignalBindingRole.ActiveRouteFragment:
                    if (signal.Value is bool isFragmentActive && isFragmentActive)
                        hasActiveSignal = true;
                    break;
                case SignalBindingRole.StartCommand:
                    if (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite)
                        isStartChecked = ReadBool(signal, isStartChecked);
                    break;
                case SignalBindingRole.StartOffFeedback:
                    isStartOff = ReadBool(signal, isStartOff);
                    break;
                case SignalBindingRole.StopCommand:
                    if (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite)
                    {
                        isStopChecked = ReadBool(signal, isStopChecked);
                        if (binding.ValueType == SignalValueType.Bool &&
                            signal.ValueType == SignalValueType.Bool && signal.Value is bool stopValue)
                            stopCommandReadback = stopValue;
                    }
                    break;
                case SignalBindingRole.StopOffFeedback:
                    isStopOff = ReadBool(signal, isStopOff);
                    break;
                case SignalBindingRole.PowerFeedback:
                    if (binding.Direction == SignalBindingDirection.Read &&
                        binding.ValueType == SignalValueType.Bool &&
                        signal.ValueType == SignalValueType.Bool && signal.Value is bool powerOn)
                        isPowerOn = powerOn;
                    break;
                case SignalBindingRole.UncheckedCommand:
                    if (!hasPowerFeedbackBinding && (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite))
                        isUncheckedSelector = ReadBool(signal, isUncheckedSelector);
                    break;
                case SignalBindingRole.CheckedCommand:
                    if (!hasPowerFeedbackBinding && (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite))
                        isCheckedSelector = ReadBool(signal, isCheckedSelector);
                    break;
                case SignalBindingRole.LoaderCommand:
                    if (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite)
                        isLoader = ReadBool(signal, isLoader ?? false);
                    break;
                case SignalBindingRole.TargetCommand:
                    if (binding.Direction is SignalBindingDirection.Read or SignalBindingDirection.ReadWrite)
                        isTarget = ReadBool(signal, isTarget ?? false);
                    break;
                case SignalBindingRole.LoaderOffFeedback:
                case SignalBindingRole.TargetOffFeedback:
                    break;
                case SignalBindingRole.AutomaticModeCommand:
                case SignalBindingRole.ManualModeCommand:
                case SignalBindingRole.ResetCommand:
                case SignalBindingRole.EmergencyCommand:
                case SignalBindingRole.AutomaticModeOffFeedback:
                case SignalBindingRole.ManualModeOffFeedback:
                case SignalBindingRole.EmergencyOffFeedback:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(binding.Role), binding.Role, "Unknown signal binding role.");
            }
        }

        foreach (var fragment in activeFragments ?? [])
        {
            var binding = fragment.Binding;
            if (!signals.TryGetValue(binding.SignalId, out var signal))
                continue;

            if (!signal.IsQualityGood || signal.IsStale)
            {
                isOffline = true;
                continue;
            }

            if (signal.Value is bool isActive && isActive)
            {
                hasActiveSignal = true;
                activeFragmentIndexes.Add(fragment.Index);
            }
        }

        var state = forceOffline || isOffline
            ? RouteObjectState.Offline
            : !isEnabled
                ? RouteObjectState.Disabled
                : forceFault || hasFault
                    ? RouteObjectState.Fault
                    : isActiveRoute && activeAsState
                        ? RouteObjectState.ActiveRoute
                        : fallbackState;

        if (isStartOff)
            isStartChecked = false;
        if (isStopOff)
            isStopChecked = false;
        if (isStartChecked && isStopChecked)
            isStartChecked = false;

        var commandsAllowed = !forceCommandsDisabled
            && state is not RouteObjectState.Offline
            && state is not RouteObjectState.Fault
            && state is not RouteObjectState.Disabled;
        if (forceOffline && !string.IsNullOrWhiteSpace(offlineText))
            text = offlineText;
        var shouldResetSelectorCommands = statusMapping is null
            && !hasPowerFeedbackBinding && hasGoodDisabledSignal && !forceOffline && !isOffline;

        EquipmentStatusPresentation? equipmentStatus = null;
        if (statusMapping is not null)
        {
            equipmentStatus = EquipmentStatusResolver.Resolve(statusMapping, statusBinding, statusSignal,
                isConnectionAvailable: state != RouteObjectState.Offline,
                hasGlobalFault: forceFault,
                hasLocalFault: hasFault || state == RouteObjectState.Fault,
                isEnabled: isEnabled && state != RouteObjectState.Disabled,
                palette: statusPalette ?? new RouteMapPaletteSettings());
            state = equipmentStatus.StateOverride ?? state;
            text = equipmentStatus.Text;
        }

        var isSelectorCommandEnabled = !hasPowerFeedbackBinding && !forceCommandsDisabled
            && state is not RouteObjectState.Offline
            && statusSignal is not ({ IsQualityGood: false } or { IsStale: true });

        return new RouteObjectRuntimeState(
            objectId,
            state,
            text,
            valueText,
            isVisible,
            canStartFallback && commandsAllowed && !isStartOff
                && (equipmentStatus is null || equipmentStatus.IsTrusted && !equipmentStatus.BlockStart),
            canStopFallback && commandsAllowed && !isStopOff
                && (equipmentStatus is null || equipmentStatus.IsTrusted && !equipmentStatus.BlockStop),
            isStartChecked,
            isStopChecked,
            hasActiveSignal,
            isLoader,
            isTarget,
            activeFragmentIndexes,
            IsSelectorChecked: !hasPowerFeedbackBinding && isEnabled && isCheckedSelector && !isUncheckedSelector,
            IsEnabled: isEnabled && state is not RouteObjectState.Offline and not RouteObjectState.Disabled,
            IsSelectorCommandEnabled: isSelectorCommandEnabled,
            ShouldResetSelectorCommands: shouldResetSelectorCommands,
            IsPowerOn: state == RouteObjectState.Offline ? null : isPowerOn)
        {
            EquipmentStatus = equipmentStatus,
            StopCommandReadback = state == RouteObjectState.Offline ? null : stopCommandReadback
        };
    }

    private static bool ReadTopBarBool(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteTopBarButtonSettings? button,
        bool fallback)
    {
        if (button is null)
            return fallback;

        var value = button.Binding.Direction == SignalBindingDirection.Write
            ? fallback
            : ReadBool(signals, button.Binding.SignalId, fallback);
        return value;
    }

    private static bool ReadTopBarEnabled(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteTopBarButtonSettings? button,
        bool isConnectionAvailable)
    {
        if (!isConnectionAvailable)
            return false;

        var binding = button?.EnabledBinding;
        if (binding is null || !signals.TryGetValue(binding.SignalId, out var signal))
            return true;

        if (!signal.IsQualityGood || signal.IsStale)
            return false;

        return signal.Value is not bool enabled || enabled;
    }

    private static TopBarCommandRuntimeState MapTopBarCommand(
        IReadOnlyDictionary<string, SignalValue> signals,
        RouteTopBarButtonSettings? button,
        SignalBindingRole role,
        bool isConnectionAvailable)
    {
        var enabledBinding = button?.EnabledBinding;
        var enabled = enabledBinding is
            { Role: SignalBindingRole.Enabled, Direction: SignalBindingDirection.Read, ValueType: SignalValueType.Bool }
            && !string.IsNullOrWhiteSpace(enabledBinding.SignalId)
                ? ReadGoodBool(signals, enabledBinding.SignalId)
                : null;
        return new TopBarCommandRuntimeState(
            isConnectionAvailable && button?.GetMomentaryCommandBinding(role) is not null
            && (enabledBinding is null || enabled == true), enabled);
    }

    private static bool? ReadGoodBool(IReadOnlyDictionary<string, SignalValue> signals, string signalId) =>
        signals.TryGetValue(signalId, out var signal) &&
        signal is { IsQualityGood: true, IsStale: false, ValueType: SignalValueType.Bool, Value: bool value }
            ? value
            : null;

    private static bool ReadBool(
        IReadOnlyDictionary<string, SignalValue> signals,
        string signalId,
        bool fallback = false)
    {
        if (!signals.TryGetValue(signalId, out var signal) || !signal.IsQualityGood || signal.IsStale)
            return fallback;

        return signal.Value is bool value ? value : fallback;
    }

    private static bool ReadConnectionAvailable(IReadOnlyDictionary<string, SignalValue> signals)
    {
        if (!signals.TryGetValue(RouteMapSystemSignalIds.ConnectionConnected, out var signal))
            return true;

        if (!signal.IsQualityGood || signal.IsStale)
            return false;

        return signal.Value is bool value && value;
    }

    private static bool ReadBool(SignalValue signal, bool fallback)
    {
        return signal.Value is bool value ? value : fallback;
    }

    private static string? ReadText(SignalValue signal, string? fallback)
    {
        return signal.Value switch
        {
            byte number => ReadEquipmentStatusText(number, fallback),
            short number => ReadEquipmentStatusText(number, fallback),
            ushort number => ReadEquipmentStatusText(number, fallback),
            int number => ReadEquipmentStatusText(number, fallback),
            uint number when number <= int.MaxValue => ReadEquipmentStatusText((int)number, fallback),
            string text when int.TryParse(text, out var number) => ReadEquipmentStatusText(number, fallback),
            string text => text,
            _ => signal.Value?.ToString() ?? fallback,
        };
    }

    private static string ReadEquipmentStatusText(int number, string? fallback)
    {
        return number switch
        {
            0 => "Выключен",
            1 => "Ожидание",
            2 => "Авария",
            3 => "Выполнение",
            4 => "Выгрузка",
            5 => "Загрузка",
            _ => fallback ?? "Выключено",
        };
    }

    private static string? ReadString(IReadOnlyDictionary<string, SignalValue> signals, string signalId)
    {
        if (!signals.TryGetValue(signalId, out var signal) || !signal.IsQualityGood || signal.IsStale)
            return null;

        return signal.Value?.ToString();
    }

    private static bool IsIgnoredRuntimeRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;
}
