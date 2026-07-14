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

    [Fact]
    public void Map_forces_nodes_and_segments_offline_when_modbus_connection_is_unavailable()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.ConnectionConnected] = new(
                RouteMapSystemSignalIds.ConnectionConnected,
                false,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
            ["equip.bucket.start"] = new(
                "equip.bucket.start",
                false,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
        };

        var runtime = mapper.Map(signals);

        Assert.False(runtime.IsConnectionAvailable);
        Assert.All(definition.Nodes, node => Assert.Equal(RouteObjectState.Offline, runtime.Find(node.Id)?.State));
        Assert.All(definition.Segments, segment => Assert.Equal(RouteObjectState.Offline, runtime.Find(segment.Id)?.State));
        Assert.False(runtime.Find("equip.bucket")?.CanStart);
        Assert.False(runtime.Find("equip.bucket")?.CanStop);
    }

    [Fact]
    public void Map_shows_card_offline_text_when_modbus_connection_is_unavailable()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.ConnectionConnected] = new(
                RouteMapSystemSignalIds.ConnectionConnected,
                false,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
            ["equip.bucket.text"] = new(
                "equip.bucket.text",
                (ushort)3,
                SignalValueType.UInt16,
                now,
                IsQualityGood: true,
                IsStale: false),
        };

        var bucket = mapper.Map(signals).Find("equip.bucket");

        Assert.Equal(RouteObjectState.Offline, bucket?.State);
        Assert.Equal("Не в сети", bucket?.Text);
        Assert.False(bucket?.CanStart);
        Assert.False(bucket?.CanStop);
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

        Assert.False(bucket?.IsStartChecked);
        Assert.True(bucket?.IsStopChecked);
    }

    [Fact]
    public void Map_uses_card_off_feedback_to_disable_and_uncheck_buttons()
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
                            SignalBindingRole.StartOffFeedback,
                            "equip.bucket.start.off",
                            SignalBindingDirection.Read,
                            SignalValueType.Bool),
                        new SignalBinding(
                            SignalBindingRole.StopOffFeedback,
                            "equip.bucket.stop.off",
                            SignalBindingDirection.Read,
                            SignalValueType.Bool)
                    ]).ToArray()
                }
            ]
        };
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.start"] = new("equip.bucket.start", true, SignalValueType.Bool, now, true, false),
            ["equip.bucket.stop"] = new("equip.bucket.stop", true, SignalValueType.Bool, now, true, false),
            ["equip.bucket.start.off"] = new("equip.bucket.start.off", true, SignalValueType.Bool, now, true, false),
            ["equip.bucket.stop.off"] = new("equip.bucket.stop.off", true, SignalValueType.Bool, now, true, false),
        };

        var bucket = mapper.Map(signals).Find("equip.bucket");

        Assert.False(bucket?.CanStart);
        Assert.False(bucket?.CanStop);
        Assert.False(bucket?.IsStartChecked);
        Assert.False(bucket?.IsStopChecked);
    }

    [Fact]
    public void Map_enabled_false_has_priority_over_fault_and_disables_object()
    {
        var seed = RouteMapSeed.Create();
        var segment = seed.Segments.Single(x => x.Id == "bsu2_to_bucket");
        var definition = seed with
        {
            Segments = seed.Segments.Select(item => item.Id == segment.Id
                ? item with
                {
                    Bindings = item.Bindings.Concat(
                    [
                        new SignalBinding(
                            SignalBindingRole.Enabled,
                            "route.bsu2_to_bucket.enabled",
                            SignalBindingDirection.Read,
                            SignalValueType.Bool)
                    ]).ToArray()
                }
                : item).ToArray()
        };
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["route.bsu2_to_bucket.enabled"] = new("route.bsu2_to_bucket.enabled", false, SignalValueType.Bool, now, true, false),
            ["bsu2_to_bucket.fault"] = new("bsu2_to_bucket.fault", true, SignalValueType.Bool, now, true, false),
            ["route.bsu2_to_bucket.active"] = new("route.bsu2_to_bucket.active", true, SignalValueType.Bool, now, true, false),
        };

        var runtime = new RouteMapRuntimeMapper(definition).Map(signals).Find(segment.Id);

        Assert.Equal(RouteObjectState.Disabled, runtime?.State);
        Assert.False(runtime?.IsEnabled);
    }

    [Fact]
    public void Map_enabled_missing_keeps_object_enabled_but_bad_quality_forces_offline()
    {
        var seed = RouteMapSeed.Create();
        var node = seed.Nodes.Single(x => x.Id == "bsu_1");
        var enabledBinding = new SignalBinding(
            SignalBindingRole.Enabled,
            "route.node.bsu_1.enabled",
            SignalBindingDirection.Read,
            SignalValueType.Bool);
        var definition = seed with
        {
            Nodes = seed.Nodes.Select(item => item.Id == node.Id
                ? item with { Bindings = item.Bindings.Append(enabledBinding).ToArray() }
                : item).ToArray()
        };

        var missing = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>()).Find(node.Id);
        var now = DateTimeOffset.UtcNow;
        var bad = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>
        {
            [enabledBinding.SignalId] = new(enabledBinding.SignalId, false, SignalValueType.Bool, now, IsQualityGood: false, IsStale: false)
        }).Find(node.Id);

        Assert.Equal(RouteObjectState.Idle, missing?.State);
        Assert.True(missing?.IsEnabled);
        Assert.Equal(RouteObjectState.Offline, bad?.State);
        Assert.False(bad?.IsEnabled);
    }

    [Fact]
    public void Map_selector_readback_requires_checked_true_and_unchecked_false()
    {
        var definition = RouteMapSeed.Create();
        var now = DateTimeOffset.UtcNow;
        var mapper = new RouteMapRuntimeMapper(definition);

        var checkedOnly = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.selector.off"] = new("equip.bucket.selector.off", false, SignalValueType.Bool, now, true, false),
            ["equip.bucket.selector.on"] = new("equip.bucket.selector.on", true, SignalValueType.Bool, now, true, false),
        }).Find("equip.bucket");
        var conflict = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.selector.off"] = new("equip.bucket.selector.off", true, SignalValueType.Bool, now, true, false),
            ["equip.bucket.selector.on"] = new("equip.bucket.selector.on", true, SignalValueType.Bool, now, true, false),
        }).Find("equip.bucket");

        Assert.True(checkedOnly?.IsSelectorChecked);
        Assert.False(conflict?.IsSelectorChecked);
    }

    [Fact]
    public void Map_card_enabled_false_requests_selector_reset_but_keeps_selector_available()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var enabledBinding = new SignalBinding(
            SignalBindingRole.Enabled,
            "equip.bucket.enabled",
            SignalBindingDirection.Read,
            SignalValueType.Bool);
        var definition = seed with
        {
            MapEquipment =
            [
                card with { Bindings = card.Bindings.Append(enabledBinding).ToArray() }
            ]
        };
        var now = DateTimeOffset.UtcNow;

        var disabled = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.enabled"] = new("equip.bucket.enabled", false, SignalValueType.Bool, now, true, false),
            ["equip.bucket.selector.off"] = new("equip.bucket.selector.off", false, SignalValueType.Bool, now, true, false),
            ["equip.bucket.selector.on"] = new("equip.bucket.selector.on", true, SignalValueType.Bool, now, true, false),
        }).Find(card.Id);
        var offline = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.ConnectionConnected] = new(RouteMapSystemSignalIds.ConnectionConnected, false, SignalValueType.Bool, now, true, false),
            ["equip.bucket.enabled"] = new("equip.bucket.enabled", false, SignalValueType.Bool, now, true, false),
        }).Find(card.Id);

        Assert.Equal(RouteObjectState.Disabled, disabled?.State);
        Assert.False(disabled?.IsEnabled);
        Assert.True(disabled?.IsSelectorCommandEnabled);
        Assert.True(disabled?.ShouldResetSelectorCommands);
        Assert.False(disabled?.IsSelectorChecked);
        Assert.Equal(RouteObjectState.Offline, offline?.State);
        Assert.False(offline?.IsSelectorCommandEnabled);
        Assert.False(offline?.ShouldResetSelectorCommands);
    }

    [Fact]
    public void Map_exposes_individual_top_bar_enabled_flags()
    {
        var seed = RouteMapSeed.Create();
        var topBar = seed.TopBar! with
        {
            Automatic = seed.TopBar!.Automatic with
            {
                EnabledBinding = new SignalBinding(SignalBindingRole.Enabled, "system.mode.automatic.enabled", SignalBindingDirection.Read, SignalValueType.Bool)
            },
            Reset = seed.TopBar.Reset with
            {
                EnabledBinding = new SignalBinding(SignalBindingRole.Enabled, "system.reset.enabled", SignalBindingDirection.Read, SignalValueType.Bool)
            },
            Emergency = seed.TopBar.Emergency with
            {
                EnabledBinding = new SignalBinding(SignalBindingRole.Enabled, "system.emergency.enabled", SignalBindingDirection.Read, SignalValueType.Bool)
            }
        };
        var definition = seed with { TopBar = topBar };
        var now = DateTimeOffset.UtcNow;
        var runtime = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.ConnectionConnected] = new(RouteMapSystemSignalIds.ConnectionConnected, true, SignalValueType.Bool, now, true, false),
            ["system.reset"] = new("system.reset", true, SignalValueType.Bool, now, true, false),
            ["system.mode.automatic.enabled"] = new("system.mode.automatic.enabled", false, SignalValueType.Bool, now, true, false),
            ["system.reset.enabled"] = new("system.reset.enabled", false, SignalValueType.Bool, now, true, false),
            ["system.emergency.enabled"] = new("system.emergency.enabled", true, SignalValueType.Bool, now, IsQualityGood: false, IsStale: false),
        });

        Assert.False(runtime.IsAutomaticCommandEnabled);
        Assert.True(runtime.IsManualCommandEnabled);
        Assert.True(runtime.IsResetActive);
        Assert.False(runtime.IsResetCommandEnabled);
        Assert.False(runtime.IsEmergencyCommandEnabled);
    }

    [Fact]
    public void Map_ignores_non_card_legacy_off_feedback_toggle_readback()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.start"] = new("equip.bucket.start", true, SignalValueType.Bool, now, true, false),
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
    public void Map_applies_global_fault_to_route_objects_and_cards()
    {
        var definition = RouteMapSeed.Create();
        var mapper = new RouteMapRuntimeMapper(definition);
        var now = DateTimeOffset.UtcNow;
        var signals = new Dictionary<string, SignalValue>
        {
            [RouteMapSystemSignalIds.GlobalFault] = new(
                RouteMapSystemSignalIds.GlobalFault,
                true,
                SignalValueType.Bool,
                now,
                IsQualityGood: true,
                IsStale: false),
            ["equip.bucket.start"] = new("equip.bucket.start", true, SignalValueType.Bool, now, true, false),
        };

        var runtime = mapper.Map(signals);

        Assert.All(definition.Nodes, node => Assert.Equal(RouteObjectState.Fault, runtime.Find(node.Id)?.State));
        Assert.All(definition.Segments, segment => Assert.Equal(RouteObjectState.Fault, runtime.Find(segment.Id)?.State));
        Assert.Equal(RouteObjectState.Fault, runtime.Find("equip.bucket")?.State);
        Assert.False(runtime.Find("equip.bucket")?.CanStart);
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
