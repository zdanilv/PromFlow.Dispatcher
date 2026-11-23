using System.Globalization;
using Avalonia.Media;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapRuntimeMapperTests
{
    [Fact]
    public void Map_reads_compatible_frequency_numbers_in_current_culture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            (SignalValueType ExpectedType, SignalValueType ActualType, object Value, string Text)[] cases =
            [
                (SignalValueType.UInt16, SignalValueType.Word, (ushort)0, "0"),
                (SignalValueType.Word, SignalValueType.UInt16, (ushort)50, "50"),
                (SignalValueType.Int16, SignalValueType.Int32, -12, "-12"),
                (SignalValueType.Int32, SignalValueType.Int16, (short)12, "12"),
                (SignalValueType.Dword, SignalValueType.Dword, uint.MaxValue, "4294967295"),
                (SignalValueType.Float32, SignalValueType.Float32, 47.5625f, "47,5625"),
            ];

            foreach (var (expectedType, actualType, value, text) in cases)
            {
                var mapper = new RouteMapRuntimeMapper(FrequencyDefinition(expectedType));
                var signal = new SignalValue("site.frequency", value, actualType, DateTimeOffset.UnixEpoch, true, false);

                Assert.Equal(text, mapper.Map(new Dictionary<string, SignalValue> { [signal.SignalId] = signal }).FrequencyValueText);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Map_rejects_unavailable_or_invalid_frequency()
    {
        var definition = FrequencyDefinition(SignalValueType.Float32);
        var mapper = new RouteMapRuntimeMapper(definition);
        var signal = new SignalValue("site.frequency", 40f, SignalValueType.Float32, DateTimeOffset.UnixEpoch, true, false);
        var signals = new Dictionary<string, SignalValue>();
        Assert.Null(mapper.Map(signals).FrequencyValueText);

        SignalValue[] invalidSignals =
        [
            signal with { IsQualityGood = false },
            signal with { IsStale = true },
            signal with { ValueType = SignalValueType.UInt16, Value = (ushort)40 },
            signal with { Value = "40" },
            signal with { Value = null },
            signal with { Value = float.NaN },
            signal with { Value = float.PositiveInfinity },
        ];
        foreach (var invalid in invalidSignals)
        {
            signals[signal.SignalId] = invalid;
            Assert.Null(mapper.Map(signals).FrequencyValueText);
        }

        signals[signal.SignalId] = signal;
        Assert.Null(new RouteMapRuntimeMapper(RouteMapSeed.Create()).Map(signals).FrequencyValueText);
        var connected = RouteMapSystemSignalIds.ConnectionConnected;
        signals[connected] = new(connected, false, SignalValueType.Bool, DateTimeOffset.UnixEpoch, true, false);
        Assert.Null(mapper.Map(signals).FrequencyValueText);
    }

    private static RouteMapDefinition FrequencyDefinition(SignalValueType valueType)
    {
        var seed = RouteMapSeed.Create();
        return seed with
        {
            Display = (seed.Display ?? new RouteMapDisplaySettings()) with
            {
                FrequencyBinding = new(SignalBindingRole.FrequencyFeedback, "site.frequency", SignalBindingDirection.Read, valueType),
            },
        };
    }

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
            "route.lower_dead_end_to_bsu1.active",
            "route.node.bsu_1.active", "route.active_bsu1_bsu2.active",
            "route.node.bsu_2.active",
            "route.bsu2_to_bucket.fragment_1.active",
            "route.bsu2_to_bucket.fragment_2.active",
            "route.bsu2_to_bucket.fragment_3.active",
            "route.node.concrete_bucket.active",
            "route.bucket_to_upper_dead_end.fragment_1.active",
            "route.bucket_to_upper_dead_end.fragment_2.active",
            "route.node.wash.active",
        };

        for (var tick = 0; tick < activeIds.Length * 2; tick++)
        {
            var snapshot = provider.CreateSnapshot(tick);
            var active = activeIds.Where(id => snapshot[id].Value is true).ToArray();
            Assert.Single(active);
            Assert.Equal(activeIds[tick % activeIds.Length], active[0]);
        }
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, null)]
    [InlineData(true, true, null)]
    public void Wash_uses_good_fresh_target_readback_and_its_own_activity(bool good, bool stale, bool? expectedTarget)
    {
        var mapper = new RouteMapRuntimeMapper(RouteMapSeed.Create());
        var now = DateTimeOffset.UtcNow;
        var snapshot = new Dictionary<string, SignalValue>
        {
            ["route.node.wash.target"] = new("route.node.wash.target", true, SignalValueType.Bool, now, good, stale),
            ["route.node.wash.active"] = new("route.node.wash.active", true, SignalValueType.Bool, now, true, false),
            ["route.node.dead_end_upper.active"] = new("route.node.dead_end_upper.active", true, SignalValueType.Bool, now, true, false),
        };

        var runtime = mapper.Map(snapshot);

        Assert.Equal(expectedTarget, runtime.Find("wash")!.IsTarget);
        Assert.True(runtime.Find("wash")!.IsSignalActive);
        Assert.Equal(good && !stale, runtime.Find("wash")!.IsEnabled);
        Assert.False(runtime.Find("dead_end_upper")!.IsSignalActive);
        Assert.False(runtime.Find("wash_to_upper_dead_end")!.IsSignalActive);
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
    [InlineData(RouteObjectState.Idle, true, false, false, "#2D56B3")]
    [InlineData(RouteObjectState.Idle, true, true, false, "#2D56B3")]
    [InlineData(RouteObjectState.Idle, true, false, true, "#2D56B3")]
    [InlineData(RouteObjectState.Idle, false, true, false, "#3A9D5D")]
    [InlineData(RouteObjectState.Idle, false, false, true, "#D99B22")]
    [InlineData(RouteObjectState.Idle, false, false, false, "#AEB5BA")]
    [InlineData(RouteObjectState.Fault, true, false, false, "#D95D4E")]
    [InlineData(RouteObjectState.Offline, true, false, false, "#3F474D")]
    [InlineData(RouteObjectState.Offline, true, true, false, "#3A9D5D")]
    [InlineData(RouteObjectState.Disabled, true, true, false, "#353535")]
    public void Node_fill_respects_signal_activity_roles_and_runtime_priority(
        RouteObjectState state, bool isActive, bool isTarget, bool isLoader, string expectedColor)
    {
        var node = new RouteNode("node", "", 0, 0, RouteNodeKind.ServicePoint, RouteObjectState.Idle, []);
        var role = new RouteNodeRoleState(node.Id, isLoader, isTarget);
        var palette = new RouteMapPaletteSettings { ActiveTrack = "#123456", Disabled = "#353535" };

        var fill = RouteMapControl.BrushForNode(node, state, isActive, role, palette);

        Assert.Equal(Color.Parse(expectedColor), Assert.IsAssignableFrom<ISolidColorBrush>(fill).Color);
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

    [Theory]
    [InlineData(3, true, false, false, "Авария системы", RouteObjectState.Fault, EquipmentStatusQuality.ForcedFault)]
    [InlineData(3, false, true, false, "Авария оборудования", RouteObjectState.Fault, EquipmentStatusQuality.ForcedFault)]
    [InlineData(2, false, false, false, "Авария", RouteObjectState.Fault, EquipmentStatusQuality.Known)]
    [InlineData(3, false, false, false, "Выполнение", RouteObjectState.Disabled, EquipmentStatusQuality.Known)]
    [InlineData(9, false, false, true, "Неизвестный статус (код 9)", RouteObjectState.Warning, EquipmentStatusQuality.UnknownCode)]
    public void Map_prioritizes_fault_disabled_and_unknown_only_for_mapped_cards(
        ushort code, bool globalFault, bool localFault, bool enabled, string text,
        RouteObjectState expectedState, EquipmentStatusQuality quality)
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var mapped = card with
        {
            Bindings = [
                .. card.Bindings,
                new(SignalBindingRole.Fault, "equip.bucket.fault", SignalBindingDirection.Read, SignalValueType.Bool),
                new(SignalBindingRole.Enabled, "equip.bucket.enabled", SignalBindingDirection.Read, SignalValueType.Bool)]
        };
        var legacy = mapped with { Id = "legacy", StatusMapping = null };
        var definition = seed with { MapEquipment = [mapped, legacy] };
        var signals = new Dictionary<string, SignalValue>
        {
            ["equip.bucket.text"] = Sample("equip.bucket.text", code, SignalValueType.UInt16),
            ["equip.bucket.fault"] = Sample("equip.bucket.fault", localFault, SignalValueType.Bool),
            ["equip.bucket.enabled"] = Sample("equip.bucket.enabled", enabled, SignalValueType.Bool),
            [RouteMapSystemSignalIds.GlobalFault] = Sample(RouteMapSystemSignalIds.GlobalFault, globalFault, SignalValueType.Bool)
        };

        var runtime = new RouteMapRuntimeMapper(definition).Map(signals);
        var status = runtime.Find(card.Id)!;
        var legacyStatus = runtime.Find(legacy.Id)!;

        Assert.Equal(text, status.Text);
        Assert.Equal(expectedState, status.State);
        Assert.Equal(quality, status.EquipmentStatus?.Quality);
        Assert.Equal(enabled, status.IsEnabled);
        Assert.False(status.CanStart);
        Assert.False(status.CanStop);
        Assert.Null(legacyStatus.EquipmentStatus);
        Assert.Equal(!enabled ? RouteObjectState.Disabled : RouteObjectState.Idle, legacyStatus.State);
        Assert.Equal(code switch { 2 => "Авария", 3 => "Выполнение", _ => card.StatusText }, legacyStatus.Text);
    }

    [Theory]
    [InlineData(true, false, true, true, false, false, false, true)]
    [InlineData(false, true, true, true, false, false, true, false)]
    [InlineData(false, false, true, true, true, false, false, true)]
    [InlineData(false, false, true, true, false, true, true, false)]
    [InlineData(false, false, false, false, false, false, false, false)]
    public void Map_intersects_status_blocks_with_card_flags_and_off_feedback(
        bool blockStart, bool blockStop, bool canStart, bool canStop, bool startOff, bool stopOff,
        bool expectedStart, bool expectedStop)
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            MapEquipment = [card with
            {
                CanStart = canStart,
                CanStop = canStop,
                StatusMapping = card.StatusMapping! with
                {
                    Entries = [new(42, "Ограничение", "#654321", EquipmentStatusCategory.Warning, blockStart, blockStop)]
                },
                Bindings = [
                    .. card.Bindings,
                    new(SignalBindingRole.StartOffFeedback, "start.off", SignalBindingDirection.Read, SignalValueType.Bool),
                    new(SignalBindingRole.StopOffFeedback, "stop.off", SignalBindingDirection.Read, SignalValueType.Bool)]
            }]
        };
        var runtime = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.text"] = Sample("equip.bucket.text", (ushort)42, SignalValueType.Word),
            ["start.off"] = Sample("start.off", startOff, SignalValueType.Bool),
            ["stop.off"] = Sample("stop.off", stopOff, SignalValueType.Bool)
        }).Find(card.Id)!;

        Assert.Equal("Ограничение", runtime.Text);
        Assert.Equal("#654321", runtime.EquipmentStatus?.Color);
        Assert.Equal(RouteObjectState.Warning, runtime.State);
        Assert.Equal(expectedStart, runtime.CanStart);
        Assert.Equal(expectedStop, runtime.CanStop);
    }

    [Fact]
    public void Map_bad_text_keeps_its_own_quality_and_does_not_invalidate_good_power_feedback()
    {
        var runtime = new RouteMapRuntimeMapper(RouteMapSeed.Create()).Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.text"] = Sample("equip.bucket.text", (ushort)3, SignalValueType.UInt16) with { IsQualityGood = false },
            ["equip.bucket.power"] = Sample("equip.bucket.power", true, SignalValueType.Bool)
        }).Find("equip.bucket")!;

        Assert.Equal(EquipmentStatusQuality.BadOrStale, runtime.EquipmentStatus?.Quality);
        Assert.Equal("Нет достоверного статуса", runtime.Text);
        Assert.True(runtime.IsPowerOn);
        Assert.False(runtime.CanStart);
        Assert.False(runtime.CanStop);
    }

    [Fact]
    public void Map_bad_text_preserves_selector_gate_for_mapped_cards_without_power_feedback()
    {
        var seed = RouteMapSeed.Create();
        var card = seed.MapEquipment.Single();
        var definition = seed with
        {
            MapEquipment = [card with { Bindings = card.Bindings.Where(binding => binding.Role != SignalBindingRole.PowerFeedback).ToArray() }]
        };
        var mapper = new RouteMapRuntimeMapper(definition);
        const string textId = "equip.bucket.text";
        var text = Sample(textId, (ushort)3, SignalValueType.UInt16);
        var runtime = mapper.Map(new Dictionary<string, SignalValue> { [textId] = text with { IsStale = true } }).Find(card.Id)!;

        Assert.Equal(EquipmentStatusQuality.BadOrStale, runtime.EquipmentStatus?.Quality);
        Assert.False(runtime.IsSelectorCommandEnabled);
        Assert.True(mapper.Map(new Dictionary<string, SignalValue> { [textId] = text }).Find(card.Id)!.IsSelectorCommandEnabled);
    }

    private static SignalValue Sample(string id, object value, SignalValueType type) =>
        new(id, value, type, DateTimeOffset.UnixEpoch, IsQualityGood: true, IsStale: false);

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
    public void Map_power_feedback_uses_good_fresh_bool_and_unknown_otherwise()
    {
        var definition = RouteMapSeed.Create();
        var now = DateTimeOffset.UtcNow;
        var mapper = new RouteMapRuntimeMapper(definition);

        var on = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.power"] = new("equip.bucket.power", true, SignalValueType.Bool, now, true, false),
        }).Find("equip.bucket");
        var off = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.power"] = new("equip.bucket.power", false, SignalValueType.Bool, now, true, false),
        }).Find("equip.bucket");
        var bad = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.power"] = new("equip.bucket.power", true, SignalValueType.Bool, now, false, false),
        }).Find("equip.bucket");
        var stale = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.power"] = new("equip.bucket.power", true, SignalValueType.Bool, now, true, true),
        }).Find("equip.bucket");
        var wrongType = mapper.Map(new Dictionary<string, SignalValue>
        {
            ["equip.bucket.power"] = new("equip.bucket.power", true, SignalValueType.UInt16, now, true, false),
        }).Find("equip.bucket");

        Assert.True(on?.IsPowerOn);
        Assert.False(off?.IsPowerOn);
        Assert.Null(bad?.IsPowerOn);
        Assert.Null(stale?.IsPowerOn);
        Assert.Null(wrongType?.IsPowerOn);
        Assert.Null(mapper.Map(new Dictionary<string, SignalValue>()).Find("equip.bucket")?.IsPowerOn);
    }

    [Fact]
    public void Map_card_enabled_false_does_not_request_legacy_selector_reset_for_bucket()
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
        Assert.False(disabled?.IsSelectorCommandEnabled);
        Assert.False(disabled?.ShouldResetSelectorCommands);
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

    [Theory]
    [InlineData(null, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void Map_requires_good_fresh_connection_for_admin_top_bar_commands(bool? connected, bool good, bool stale)
    {
        var mapper = new RouteMapRuntimeMapper(RouteMapSeed.Create());
        var connectionId = RouteMapSystemSignalIds.ConnectionConnected;
        var signals = new Dictionary<string, SignalValue>();
        if (connected.HasValue)
            signals[connectionId] = new(connectionId, connected, SignalValueType.Bool, DateTimeOffset.UtcNow, good, stale);

        var runtime = mapper.Map(signals);

        Assert.False(runtime.UnloadCommand.CanExecute);
        Assert.False(runtime.RotationCommand.CanExecute);
        Assert.False(runtime.LoadCommand.CanExecute);
        Assert.Equal(default, mapper.Map(null).UnloadCommand);
    }

    [Fact]
    public void Map_exposes_independent_admin_top_bar_enabled_signals()
    {
        var seed = RouteMapSeed.Create();
        var topBar = seed.TopBar!;
        var definition = seed with
        {
            TopBar = topBar with
            {
                Rotation = topBar.Rotation with { EnabledBinding = Enabled("test.rotate.enabled") },
                Load = topBar.Load with { EnabledBinding = Enabled("test.load.enabled") },
            },
        };
        var connectionId = RouteMapSystemSignalIds.ConnectionConnected;
        var now = DateTimeOffset.UtcNow;
        var runtime = new RouteMapRuntimeMapper(definition).Map(new Dictionary<string, SignalValue>
        {
            [connectionId] = new(connectionId, true, SignalValueType.Bool, now, true, false),
            ["test.rotate.enabled"] = new("test.rotate.enabled", true, SignalValueType.Bool, now, true, false),
            ["test.load.enabled"] = new("test.load.enabled", false, SignalValueType.Bool, now, true, false),
        });

        Assert.Equal(new TopBarCommandRuntimeState(true, null), runtime.UnloadCommand);
        Assert.Equal(new TopBarCommandRuntimeState(true, true), runtime.RotationCommand);
        Assert.Equal(new TopBarCommandRuntimeState(false, false), runtime.LoadCommand);

        static SignalBinding Enabled(string id) =>
            new(SignalBindingRole.Enabled, id, SignalBindingDirection.Read, SignalValueType.Bool);
    }

    [Fact]
    public void Mock_provider_supplies_configured_admin_commands_and_enabled_signals()
    {
        var seed = RouteMapSeed.Create();
        var topBar = seed.TopBar!;
        var definition = seed with
        {
            TopBar = topBar with
            {
                Unload = WithEnabled(topBar.Unload),
                Rotation = WithEnabled(topBar.Rotation),
                Load = WithEnabled(topBar.Load),
            },
        };
        var snapshot = new MockSignalProvider(definition).CreateSnapshot(0);
        var runtime = new RouteMapRuntimeMapper(definition).Map(snapshot);

        foreach (var settings in new[] { topBar.Unload, topBar.Rotation, topBar.Load })
            Assert.Equal(false, snapshot[settings.Binding.SignalId].Value);
        Assert.Equal(new TopBarCommandRuntimeState(true, true), runtime.UnloadCommand);
        Assert.Equal(new TopBarCommandRuntimeState(true, true), runtime.RotationCommand);
        Assert.Equal(new TopBarCommandRuntimeState(true, true), runtime.LoadCommand);

        static RouteTopBarButtonSettings WithEnabled(RouteTopBarButtonSettings settings) => settings with
        {
            EnabledBinding = new(SignalBindingRole.Enabled, $"{settings.Binding.SignalId}.enabled",
                SignalBindingDirection.Read, SignalValueType.Bool),
        };
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
            ["equip.bucket.text"] = Sample("equip.bucket.text", (ushort)3, SignalValueType.UInt16),
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
