using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class RouteMapRuntimeMapper : IRouteMapRuntimeMapper<RouteMapRuntimeState>
{
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

    public RouteMapRuntimeState Map(IReadOnlyDictionary<string, SignalValue> signals)
    {
        var objects = new Dictionary<string, RouteObjectRuntimeState>();
        var definition = _configurationManager?.CurrentDefinition ?? _fixedDefinition
            ?? throw new InvalidOperationException("RouteMap definition is unavailable.");
        var isConnectionAvailable = ReadConnectionAvailable(signals);
        var isGlobalFaultActive = ReadBool(signals, RouteMapSystemSignalIds.GlobalFault);

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
                forceCommandsDisabled: !isConnectionAvailable);
        }

        return new RouteMapRuntimeState(
            objects,
            IsAutomaticMode: ReadTopBarBool(signals, definition.TopBar?.Automatic, fallback: false),
            IsManualMode: ReadTopBarBool(signals, definition.TopBar?.Manual, fallback: true),
            HasEmergency: ReadTopBarBool(signals, definition.TopBar?.Emergency, fallback: false),
            IsQueueRunning: ReadBool(signals, RouteMapSystemSignalIds.QueueRunning),
            ConnectionStatusText: ReadString(signals, RouteMapSystemSignalIds.ConnectionStatus) ?? "Ожидание",
            IsConnectionAvailable: isConnectionAvailable);
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
        bool forceCommandsDisabled = false)
    {
        var text = textFallback;
        string? valueText = null;
        var isVisible = true;
        var isStartChecked = false;
        var isStopChecked = false;
        var isStartOff = false;
        var isStopOff = false;
        var isOffline = false;
        var hasFault = false;
        var isActiveRoute = false;
        var hasActiveSignal = false;
        bool? isLoader = null;
        bool? isTarget = null;
        var activeFragmentIndexes = new HashSet<int>();

        foreach (var binding in bindings)
        {
            if (IsIgnoredRuntimeRole(binding.Role))
                continue;

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
                        isStopChecked = ReadBool(signal, isStopChecked);
                    break;
                case SignalBindingRole.StopOffFeedback:
                    isStopOff = ReadBool(signal, isStopOff);
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

        return new RouteObjectRuntimeState(
            objectId,
            state,
            text,
            valueText,
            isVisible,
            canStartFallback && commandsAllowed && !isStartOff,
            canStopFallback && commandsAllowed && !isStopOff,
            isStartChecked,
            isStopChecked,
            hasActiveSignal,
            isLoader,
            isTarget,
            activeFragmentIndexes);
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
