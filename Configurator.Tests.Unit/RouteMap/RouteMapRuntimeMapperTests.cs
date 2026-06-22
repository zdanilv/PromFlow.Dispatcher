using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapRuntimeMapperTests
{
    [Fact]
    public void Map_marks_active_route_from_boolean_signal()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["route.active_bsu1_bsu2.active"] = new(
                "route.active_bsu1_bsu2.active",
                true,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);

        Assert.Equal(RouteObjectState.ActiveRoute, runtime.Find("active_bsu1_bsu2")?.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Map_applies_fault_and_active_priority_independent_of_binding_order(bool reverse)
    {
        var seed = RouteMapSeed.Create();
        var source = seed.Segments.Single(x => x.Id == "bsu2_to_bucket");
        var bindings = source.Bindings.ToArray();
        if (reverse)
            Array.Reverse(bindings);
        var definition = seed with
        {
            Segments = seed.Segments.Select(x => x.Id == source.Id ? x with { Bindings = bindings } : x).ToArray(),
        };
        var now = DateTimeOffset.UtcNow;
        var signals = bindings.ToDictionary(
            binding => binding.SignalId,
            binding => new SignalValue(
                binding.SignalId,
                binding.Role switch
                {
                    SignalBindingRole.Fault => true,
                    SignalBindingRole.ActiveRoute => true,
                    _ => false,
                },
                binding.ValueType,
                now,
                IsQualityGood: true,
                IsStale: false));

        var runtime = new RouteMapRuntimeMapper(definition).Map(signals);

        Assert.Equal(RouteObjectState.Fault, runtime.Find(source.Id)?.State);
    }

    [Fact]
    public void Map_marks_only_active_fragment_without_promoting_whole_segment_state()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["route.bsu2_to_bucket.fragment_2.active"] = new(
                "route.bsu2_to_bucket.fragment_2.active",
                true,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);
        var segment = runtime.Find("bsu2_to_bucket");
        var ranges = new[]
        {
            new RoutePathRange(0, 10),
            new RoutePathRange(20, 30),
            new RoutePathRange(40, 50),
            new RoutePathRange(60, 70),
        };

        var activeRanges = RouteMapControl.ActiveSegmentRanges(segment!.State, segment, ranges);

        Assert.Equal(RouteObjectState.Idle, segment.State);
        Assert.True(segment.IsSignalActive);
        Assert.Equal([2], segment.ActiveFragmentIndexes);
        Assert.Equal([ranges[1]], activeRanges);
    }

    [Fact]
    public void Fragment_overlay_selects_only_matching_visual_range_index()
    {
        var runtime = new RouteObjectRuntimeState(
            "segment",
            RouteObjectState.Idle,
            null,
            null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsSignalActive: true,
            ActiveFragmentIndexes: new HashSet<int> { 2 });
        var visualRanges = new[]
        {
            new RoutePathRange(20, 120),
            new RoutePathRange(140, 240),
        };

        var activeRanges = RouteMapControl.ActiveSegmentRanges(
            runtime.State,
            runtime,
            visualRanges);

        Assert.Equal([visualRanges[1]], activeRanges);
    }

    [Fact]
    public void Fault_state_suppresses_fragment_range_overlay()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["bsu2_to_bucket.fault"] = new("bsu2_to_bucket.fault", true, SignalValueType.Bool, now, true, false),
            ["route.bsu2_to_bucket.fragment_2.active"] = new("route.bsu2_to_bucket.fragment_2.active", true, SignalValueType.Bool, now, true, false),
        };

        var segment = mapper.Map(signals).Find("bsu2_to_bucket");
        var activeRanges = RouteMapControl.ActiveSegmentRanges(
            segment!.State,
            segment,
            [new RoutePathRange(0, 10), new RoutePathRange(20, 30)]);

        Assert.Equal(RouteObjectState.Fault, segment.State);
        Assert.Empty(activeRanges);
    }

    [Fact]
    public void Map_prioritizes_offline_quality_over_fault_and_active_route()
    {
        var definition = RouteMapSeed.Create();
        var segment = definition.Segments.Single(x => x.Id == "bsu2_to_bucket");
        var now = DateTimeOffset.UtcNow;
        var signals = segment.Bindings.ToDictionary(
            binding => binding.SignalId,
            binding => new SignalValue(
                binding.SignalId,
                true,
                binding.ValueType,
                now,
                IsQualityGood: binding.Role != SignalBindingRole.ActiveRoute,
                IsStale: false));

        var runtime = new RouteMapRuntimeMapper(definition).Map(signals);

        Assert.Equal(RouteObjectState.Offline, runtime.Find(segment.Id)?.State);
    }

    [Fact]
    public void Mock_activates_exactly_one_route_object_per_snapshot_in_chain_order()
    {
        var definition = RouteMapSeed.Create();
        var provider = new MockSignalProvider(definition);
        var activeIds = new[]
        {
            "route.node.dead_end_lower.active", "route.lower_dead_end_to_bsu1.active",
            "route.node.bsu_1.active", "route.active_bsu1_bsu2.active",
            "route.node.bsu_2.active",
            "route.bsu2_to_bucket.fragment_1.active",
            "route.bsu2_to_bucket.fragment_2.active",
            "route.bsu2_to_bucket.fragment_3.active",
            "route.node.concrete_bucket.active",
            "route.bucket_to_upper_dead_end.fragment_1.active",
            "route.bucket_to_upper_dead_end.fragment_2.active",
            "route.node.dead_end_upper.active",
        };

        for (var tick = 0; tick < activeIds.Length * 2; tick++)
        {
            var snapshot = provider.CreateSnapshot(tick);
            var active = activeIds.Where(id => snapshot[id].Value is true).ToArray();
            Assert.Single(active);
            Assert.Equal(activeIds[tick % activeIds.Length], active[0]);
        }
    }

    [Fact]
    public void Map_keeps_node_fill_state_and_exposes_signal_activity_and_role_readback()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["route.node.bsu_1.active"] = new("route.node.bsu_1.active", true, SignalValueType.Bool, now, true, false),
            ["route.node.bsu_1.loader"] = new("route.node.bsu_1.loader", false, SignalValueType.Bool, now, true, false),
            ["route.node.bsu_1.target"] = new("route.node.bsu_1.target", true, SignalValueType.Bool, now, true, false),
        };

        var node = mapper.Map(signals).Find("bsu_1");

        Assert.Equal(RouteObjectState.Idle, node?.State);
        Assert.True(node?.IsSignalActive);
        Assert.False(node?.IsLoader);
        Assert.True(node?.IsTarget);
    }

    [Fact]
    public async Task Mock_command_dispatcher_publishes_written_value_as_readback()
    {
        var state = new MockSignalState();
        var provider = new MockSignalProvider(RouteMapSeed.Create(), state);
        var dispatcher = new MockEquipmentCommandDispatcher(state);

        await dispatcher.DispatchAsync(new SignalWriteRequest("route.node.bsu_1.target", true, SignalValueType.Bool));
        var snapshot = provider.CreateSnapshot(0);

        Assert.True(snapshot["route.node.bsu_1.target"].Value is true);
    }

    [Theory]
    [InlineData(RouteObjectState.Idle, true)]
    [InlineData(RouteObjectState.Fault, false)]
    [InlineData(RouteObjectState.Offline, false)]
    [InlineData(RouteObjectState.Disabled, false)]
    public void Node_active_outline_respects_runtime_priority(RouteObjectState state, bool expected)
    {
        var runtime = new RouteObjectRuntimeState("node", state, null, null, true, false, false, IsSignalActive: true);
        Assert.Equal(expected, RouteMapControl.ShouldDrawActiveOutline(runtime));
    }

    [Fact]
    public void Map_blocks_commands_when_equipment_is_offline()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.start"] = new(
                "equip.bucket.start",
                true,
                SignalValueType.Bool,
                now,
                IsQualityGood: false,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);
        var bucket = runtime.Find("equip.bucket");

        Assert.Equal(RouteObjectState.Offline, bucket?.State);
        Assert.False(bucket?.CanStart);
    }

    [Theory]
    [InlineData(0, "Выключен")]
    [InlineData(1, "Ожидание")]
    [InlineData(2, "Авария")]
    [InlineData(3, "Выполнение")]
    [InlineData(4, "Выгрузка")]
    [InlineData(5, "Загрузка")]
    public void Map_converts_bucket_status_code_to_text(ushort code, string expectedText)
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.text"] = new(
                "equip.bucket.text",
                code,
                SignalValueType.UInt16,
                now,
                IsQualityGood: true,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);

        Assert.Equal(expectedText, runtime.Find("equip.bucket")?.Text);
    }

    [Fact]
    public void Map_reads_bucket_start_and_stop_checked_state()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.start"] = new(
                "equip.bucket.start",
                true,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
            ["equip.bucket.stop"] = new(
                "equip.bucket.stop",
                true,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);
        var bucket = runtime.Find("equip.bucket");

        Assert.True(bucket?.IsStartChecked);
        Assert.True(bucket?.IsStopChecked);
    }

    [Fact]
    public void Map_ignores_legacy_off_feedback_toggle_readback()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.start"] = new("equip.bucket.start", true, SignalValueType.Bool, now, true, false),
            ["equip.bucket.start.off"] = new("equip.bucket.start.off", true, SignalValueType.Bool, now, true, false),
            ["route.node.bsu_1.loader"] = new("route.node.bsu_1.loader", true, SignalValueType.Bool, now, true, false),
            ["route.node.bsu_1.loader.off"] = new("route.node.bsu_1.loader.off", true, SignalValueType.Bool, now, true, false),
            ["system.mode.manual"] = new("system.mode.manual", true, SignalValueType.Bool, now, true, false),
            ["system.mode.manual.off"] = new("system.mode.manual.off", true, SignalValueType.Bool, now, true, false),
            ["system.emergency"] = new("system.emergency", true, SignalValueType.Bool, now, true, false),
            ["system.emergency.off"] = new("system.emergency.off", true, SignalValueType.Bool, now, true, false),
        };

        var runtime = mapper.Map(signals);

        Assert.True(runtime.Find("equip.bucket")?.IsStartChecked);
        Assert.True(runtime.Find("bsu_1")?.IsLoader);
        Assert.True(runtime.IsManualMode);
        Assert.True(runtime.HasEmergency);
    }

    [Fact]
    public void Map_ignores_legacy_state_binding_and_quality()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            MapEquipment =
            [
                card with
                {
                    Bindings = card.Bindings.Concat(
                    [
                        new SignalBinding(
                            SignalBindingRole.State,
                            "equip.bucket.state",
                            SignalBindingDirection.Read,
                            SignalValueType.UInt16)
                    ]).ToArray()
                }
            ]
        };
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.state"] = new(
                "equip.bucket.state",
                (ushort)3,
                SignalValueType.UInt16,
                now,
                IsQualityGood: false,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);
        var bucket = runtime.Find("equip.bucket");

        Assert.Equal(RouteObjectState.Idle, bucket?.State);
        Assert.True(bucket?.CanStart);
    }
}
