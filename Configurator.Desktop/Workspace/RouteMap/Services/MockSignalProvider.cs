using System.Reactive.Linq;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;

namespace Configurator.Desktop.Workspace.RouteMap.Services;

public sealed class MockSignalProvider : ISignalValueProvider
{
    private readonly RouteMapConfigurationManager? _configurationManager;
    private readonly RouteMapDefinition? _fixedDefinition;
    private readonly MockSignalState _state;

    public MockSignalProvider(RouteMapConfigurationManager configurationManager, MockSignalState state)
    {
        _configurationManager = configurationManager;
        _state = state;
    }

    public MockSignalProvider(RouteMapDefinition definition)
        : this(definition, new MockSignalState())
    {
    }

    public MockSignalProvider(RouteMapDefinition definition, MockSignalState state)
    {
        _fixedDefinition = definition;
        _state = state;
    }

    public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe()
    {
        return Observable
            .Interval(TimeSpan.FromSeconds(2))
            .StartWith(0)
            .Select(tick => CreateSnapshot((int)tick));
    }

    internal IReadOnlyDictionary<string, SignalValue> CreateSnapshot(int tick)
    {
        var now = DateTimeOffset.Now;
        var signals = new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.QueueRunning] = Bool(RouteMapSystemSignalIds.QueueRunning, tick % 8 is >= 3 and <= 5, now),
            [RouteMapSystemSignalIds.ConnectionStatus] = String(RouteMapSystemSignalIds.ConnectionStatus, "Ожидание", now),
            [RouteMapSystemSignalIds.ConnectionConnected] = Bool(RouteMapSystemSignalIds.ConnectionConnected, true, now),
            [RouteMapSystemSignalIds.GlobalFault] = Bool(RouteMapSystemSignalIds.GlobalFault, false, now),
        };

        var definition = _configurationManager?.CurrentDefinition ?? _fixedDefinition
            ?? throw new InvalidOperationException("RouteMap definition is unavailable.");
        var activeRouteSignalIds = CreateActiveRouteOrder(definition);
        var activeRouteSignalId = activeRouteSignalIds.Length == 0
            ? null
            : activeRouteSignalIds[Math.Abs(tick) % activeRouteSignalIds.Length];
        var bindings = definition.Nodes.SelectMany(x => x.Bindings)
            .Concat(definition.Segments.SelectMany(x => x.Bindings))
            .Concat(definition.Segments.SelectMany(x => x.ActiveFragments ?? []).Select(x => x.Binding))
            .Concat(definition.Vehicles.SelectMany(x => x.Bindings))
            .Concat(definition.MapEquipment.SelectMany(x => x.Bindings))
            .Concat(TopBarBindings(definition))
            .Where(x => !IsDeprecatedSignalRole(x.Role))
            .GroupBy(x => x.SignalId, StringComparer.Ordinal)
            .Select(x => x.First());

        foreach (var binding in bindings)
            signals[binding.SignalId] = binding.Role is SignalBindingRole.ActiveRoute or SignalBindingRole.ActiveRouteFragment
                ? Bool(binding.SignalId, binding.SignalId == activeRouteSignalId, now)
                : CreateValue(binding, definition, tick, now);

        return signals;
    }

    private SignalValue CreateValue(
        SignalBinding binding,
        RouteMapDefinition definition,
        int tick,
        DateTimeOffset timestamp)
    {
        if (_state.TryGet(binding.SignalId, out var written))
            return new SignalValue(binding.SignalId, written.Value, written.ValueType, timestamp, IsQualityGood: true, IsStale: false);

        object value = binding.Role switch
        {
            SignalBindingRole.Text => (ushort)(tick % 6),
            SignalBindingRole.Value => 0,
            SignalBindingRole.Visible => true,
            SignalBindingRole.Fault => false,
            SignalBindingRole.ActiveRoute => true,
            SignalBindingRole.ActiveRouteFragment => false,
            SignalBindingRole.StartCommand => false,
            SignalBindingRole.StartOffFeedback => false,
            SignalBindingRole.StopCommand => false,
            SignalBindingRole.StopOffFeedback => false,
            SignalBindingRole.TargetCommand => definition.Nodes.Any(x => x.IsTarget && x.Bindings.Any(candidate => candidate.SignalId == binding.SignalId)),
            SignalBindingRole.LoaderCommand => definition.Nodes.Any(x => x.IsLoader && x.Bindings.Any(candidate => candidate.SignalId == binding.SignalId)),
            SignalBindingRole.AutomaticModeCommand => false,
            SignalBindingRole.ManualModeCommand => true,
            SignalBindingRole.EmergencyCommand => tick % 20 == 12,
            SignalBindingRole.EmergencyOffFeedback => false,
            _ => false,
        };

        return new SignalValue(binding.SignalId, value, binding.ValueType, timestamp, IsQualityGood: true, IsStale: false);
    }

    private static string[] CreateActiveRouteOrder(RouteMapDefinition definition)
    {
        var nodes = definition.Nodes.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var segments = definition.Segments.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var orderedObjects = new List<(string Id, IReadOnlyList<SignalBinding> Bindings)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var chain in definition.Chains)
        {
            var count = Math.Max(chain.NodeIds.Count, chain.SegmentIds.Count);
            for (var index = 0; index < count; index++)
            {
                if (index < chain.NodeIds.Count && nodes.TryGetValue(chain.NodeIds[index], out var node) && seen.Add("node:" + node.Id))
                    orderedObjects.Add((node.Id, node.Bindings));
                if (index < chain.SegmentIds.Count && segments.TryGetValue(chain.SegmentIds[index], out var segment) && seen.Add("segment:" + segment.Id))
                {
                    if (segment.ActiveFragments is null || segment.ActiveFragments.Count == 0)
                        orderedObjects.Add((segment.Id, segment.Bindings));

                    foreach (var fragment in (segment.ActiveFragments ?? []).OrderBy(fragment => fragment.Index))
                        orderedObjects.Add(($"{segment.Id}.fragment_{fragment.Index}", [fragment.Binding]));
                }
            }
        }

        foreach (var node in definition.Nodes)
            if (seen.Add("node:" + node.Id))
                orderedObjects.Add((node.Id, node.Bindings));
        foreach (var segment in definition.Segments)
            if (seen.Add("segment:" + segment.Id))
            {
                if (segment.ActiveFragments is null || segment.ActiveFragments.Count == 0)
                    orderedObjects.Add((segment.Id, segment.Bindings));

                foreach (var fragment in (segment.ActiveFragments ?? []).OrderBy(fragment => fragment.Index))
                    orderedObjects.Add(($"{segment.Id}.fragment_{fragment.Index}", [fragment.Binding]));
            }

        return orderedObjects
            .Select(x => x.Bindings.FirstOrDefault(binding =>
                binding.Role is SignalBindingRole.ActiveRoute or SignalBindingRole.ActiveRouteFragment)?.SignalId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<SignalBinding> TopBarBindings(RouteMapDefinition definition)
    {
        if (definition.TopBar is null)
            return [];
        return new[]
        {
            definition.TopBar.Automatic.Binding,
            definition.TopBar.Manual.Binding,
            definition.TopBar.Emergency.Binding,
        }.OfType<SignalBinding>();
    }

    private static SignalValue Bool(string signalId, bool value, DateTimeOffset timestamp)
    {
        return new SignalValue(signalId, value, SignalValueType.Bool, timestamp, IsQualityGood: true, IsStale: false);
    }

    private static SignalValue String(string signalId, string value, DateTimeOffset timestamp)
    {
        return new SignalValue(signalId, value, SignalValueType.String, timestamp, IsQualityGood: true, IsStale: false);
    }

    private static SignalValue UInt16(string signalId, ushort value, DateTimeOffset timestamp)
    {
        return new SignalValue(signalId, value, SignalValueType.UInt16, timestamp, IsQualityGood: true, IsStale: false);
    }

    private static bool IsDeprecatedSignalRole(SignalBindingRole role) => role is
        SignalBindingRole.State or
        SignalBindingRole.TargetOffFeedback or
        SignalBindingRole.LoaderOffFeedback or
        SignalBindingRole.AutomaticModeOffFeedback or
        SignalBindingRole.ManualModeOffFeedback or
        SignalBindingRole.EmergencyOffFeedback;
}
