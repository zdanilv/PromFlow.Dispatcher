using System.Reactive.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Dialogs.HelpDialog;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapConfigurationTests
{
    [Theory]
    [InlineData(RouteTerminalSide.From, 28d, true)]
    [InlineData(RouteTerminalSide.To, null, false)]
    public async Task DeadEnd_round_trips_geometry_and_main_track_bindings(
        RouteTerminalSide side, double? capLength, bool active)
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var editor = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        var (anchor, segment) = ConfigureTerminal(editor.Draft, side);
        segment.TerminalCapLength = capLength;
        segment.Title = "ТУПИК";
        segment.LabelOffsetX = 17;
        segment.Style.Thickness = 5;
        segment.Style.NormalColor = "#68717A";
        if (!active)
        {
            segment.Bindings.Clear();
            segment.ActiveFragments.Clear();
        }

        var bindingIds = segment.Bindings.Select(binding => binding.SignalId).ToArray();
        var anchorSignalIds = RouteMapSeed.Create().Nodes.Single(node => node.Id == anchor.Id)
            .Bindings.Select(binding => binding.SignalId).ToArray();
        await editor.SaveAsync();
        Assert.Null(editor.ErrorMessage);

        using var reopened = scope.CreateManager();
        Assert.Null(reopened.LastLoadError);
        var definition = reopened.CurrentDefinition;
        var modelAnchor = definition.Nodes.Single(node => node.Id == anchor.Id);
        Assert.Equal(RouteNodeKind.TerminalAnchor, modelAnchor.Kind);
        Assert.Equal(RouteNodeMenuKind.None, modelAnchor.MenuKind);
        Assert.Empty(modelAnchor.Bindings);
        var model = definition.Segments.Single(line => line.Id == segment.Id);
        Assert.Equal(RouteSegmentKind.DeadEnd, model.Kind);
        Assert.Equal(side, model.TerminalSide);
        Assert.Equal(capLength, model.TerminalCapLength);
        Assert.Equal(bindingIds, model.Bindings.Select(binding => binding.SignalId));
        Assert.Empty(model.ActiveFragments!);
        Assert.Equal(segment.Title, model.Title);
        Assert.Equal(segment.LabelOffsetX, model.LabelOffsetX);
        Assert.Equal(5, model.Style!.Thickness);
        Assert.Equal("#68717A", model.Style.NormalColor);

        var inventory = RouteMapSignalInventory.Build(definition);
        Assert.DoesNotContain(inventory, item => anchorSignalIds.Contains(item.SignalId));
        Assert.All(bindingIds, id => Assert.Contains(inventory, item => item.SignalId == id));

        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!;
        var storedNode = persisted["nodes"]!.AsArray().Single(node => (string?)node!["id"] == anchor.Id)!;
        Assert.Equal("terminalAnchor", (string?)storedNode["kind"]);
        var storedLine = persisted["segments"]!.AsArray().Single(line => (string?)line!["id"] == segment.Id)!;
        Assert.Equal("deadEnd", (string?)storedLine["kind"]);
        Assert.Equal(side == RouteTerminalSide.From ? "from" : "to", (string?)storedLine["terminalSide"]);
        Assert.Equal(capLength, (double?)storedLine["terminalCapLength"]);
        Assert.Equal(capLength.HasValue, storedLine.AsObject().ContainsKey("terminalCapLength"));

        var mapped = scope.Mapper.ToDocument(definition).Segments.Single(line => line.Id == segment.Id);
        Assert.Equal(side, mapped.TerminalSide);
        Assert.Equal(capLength, mapped.TerminalCapLength);
    }

    [Fact]
    public void DeadEnd_preserves_fragments_on_an_active_main_track_only()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var (_, segment) = ConfigureTerminal(document, RouteTerminalSide.To);
        segment.TerminalCapLength = 1000;
        var fragment = segment.ActiveFragments[0];
        fragment.Binding.SignalId = "custom.fragment.active";
        RouteSegmentActiveFragmentSynchronizer.Ensure(document, segment);

        Assert.Equal(2, segment.ActiveFragments.Count);
        Assert.Same(fragment, segment.ActiveFragments[0]);
        Assert.Equal("custom.fragment.active", fragment.Binding.SignalId);
        var validator = new RouteMapConfigurationValidator();
        Assert.Empty(validator.Validate(document).Errors);

        segment.Bindings.Remove(segment.Bindings.Single(binding => binding.Role == SignalBindingRole.ActiveRoute));
        Assert.Contains(validator.Validate(document).Errors, error =>
            error.ObjectId == segment.Id && error.Property == nameof(segment.ActiveFragments));
        segment.ActiveFragments.Clear();
        Assert.Empty(validator.Validate(document).Errors);
    }

    [Theory]
    [InlineData(1e9)]
    [InlineData(1e12)]
    public void Validator_rejects_terminal_fragment_count_mismatch_without_generating_bindings(double anchorX)
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var (anchor, segment) = ConfigureTerminal(document, RouteTerminalSide.From);
        anchor.X = anchorX;

        RouteSegmentActiveFragmentSynchronizer.Ensure(document, segment);

        Assert.Empty(segment.ActiveFragments);
        Assert.Contains(new RouteMapConfigurationValidator().Validate(document).Errors, error =>
            error.ObjectId == segment.Id && error.Property == nameof(segment.ActiveFragments));
    }

    [Fact]
    public void Validator_checks_terminal_ownership_and_anchor_roles()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var (anchor, segment) = ConfigureTerminal(document, RouteTerminalSide.From);
        segment.TerminalSide = RouteTerminalSide.To;
        anchor.IsLoader = true;
        anchor.IsTarget = true;
        anchor.MenuKind = RouteNodeMenuKind.SendOnly;
        anchor.State = RouteObjectState.ActiveRoute;
        anchor.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.TargetCommand,
            SignalId = "invalid.anchor.target",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Bool,
        });
        var otherLine = document.Segments.Single(line => line.Id == "active_bsu1_bsu2");
        otherLine.FromNodeId = anchor.Id;

        var errors = new RouteMapConfigurationValidator().Validate(document).Errors;
        var anchorProperties = errors.Where(error => error.ObjectId == anchor.Id).Select(error => error.Property).ToArray();
        Assert.All(new[] { nameof(anchor.Id), nameof(anchor.IsLoader), nameof(anchor.IsTarget), nameof(anchor.MenuKind), nameof(anchor.State), nameof(anchor.Bindings) },
            property => Assert.Contains(property, anchorProperties));
        Assert.Contains(errors, error => error.ObjectId == segment.Id && error.Property == nameof(segment.TerminalSide));
        Assert.Contains(errors, error => error.ObjectId == otherLine.Id && error.Property == nameof(otherLine.Kind));
    }

    [Fact]
    public void Validator_requires_a_terminal_side_and_a_station_at_the_other_end()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var (_, segment) = ConfigureTerminal(document, RouteTerminalSide.From);
        var validator = new RouteMapConfigurationValidator();
        segment.TerminalSide = null;
        Assert.Contains(validator.Validate(document).Errors, error =>
            error.ObjectId == segment.Id && error.Property == nameof(segment.TerminalSide));

        segment.TerminalSide = RouteTerminalSide.From;
        var secondAnchor = new RouteNodeConfiguration { Id = "second_anchor", Kind = RouteNodeKind.TerminalAnchor };
        document.Nodes.Add(secondAnchor);
        segment.ToNodeId = secondAnchor.Id;
        Assert.Contains(validator.Validate(document).Errors, error =>
            error.ObjectId == segment.Id && error.Property == nameof(segment.TerminalSide));

        segment.ToNodeId = "missing_station";
        Assert.Contains(validator.Validate(document).Errors, error =>
            error.ObjectId == segment.Id && error.Property == nameof(segment.ToNodeId));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Validator_rejects_invalid_terminal_geometry_and_active_binding(double length)
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var (anchor, segment) = ConfigureTerminal(document, RouteTerminalSide.From);
        var station = document.Nodes.Single(node => node.Id == segment.ToNodeId);
        (anchor.X, anchor.Y) = (station.X, station.Y);
        segment.TerminalCapLength = length;
        segment.Style.Thickness = length;
        var active = segment.Bindings.Single(binding => binding.Role == SignalBindingRole.ActiveRoute);
        active.Direction = SignalBindingDirection.ReadWrite;
        active.ValueType = SignalValueType.UInt16;

        var properties = new RouteMapConfigurationValidator().Validate(document).Errors
            .Where(error => error.ObjectId == segment.Id).Select(error => error.Property).ToArray();
        Assert.Contains(nameof(segment.ToNodeId), properties);
        Assert.Contains(nameof(segment.TerminalCapLength), properties);
        Assert.Contains(nameof(segment.Style.Thickness), properties);
        Assert.Contains(nameof(active.Direction), properties);
        Assert.Contains(nameof(active.ValueType), properties);
    }

    private static (RouteNodeConfiguration Anchor, RouteSegmentConfiguration Segment) ConfigureTerminal(
        RouteMapConfigurationDocument document, RouteTerminalSide side)
    {
        var segment = document.Segments.Single(line => line.Id == (side == RouteTerminalSide.From
            ? "lower_dead_end_to_bsu1"
            : "bucket_to_upper_dead_end"));
        if (side == RouteTerminalSide.To)
        {
            document.Segments.Remove(document.Segments.Single(line => line.Id == "wash_to_upper_dead_end"));
            document.Nodes.Remove(document.Nodes.Single(node => node.Id == "dead_end_upper"));
            document.Chains.Single().NodeIds.Remove("dead_end_upper");
            document.Chains.Single().SegmentIds.Remove("wash_to_upper_dead_end");
        }
        var anchor = document.Nodes.Single(node => node.Id == (side == RouteTerminalSide.From
            ? segment.FromNodeId
            : segment.ToNodeId));
        anchor.Kind = RouteNodeKind.TerminalAnchor;
        anchor.MenuKind = RouteNodeMenuKind.None;
        anchor.Bindings.Clear();
        segment.Kind = RouteSegmentKind.DeadEnd;
        segment.TerminalSide = side;
        return (anchor, segment);
    }

    private static RouteMapConfigurationDocument CreateLegacyTopology(TempConfigurationScope scope, int version)
    {
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = version;
        var lower = document.Nodes.Single(node => node.Id == "dead_end_lower");
        var upper = document.Nodes.Single(node => node.Id == "dead_end_upper");
        lower.Y = 500;
        upper.X = 830;
        foreach (var anchor in new[] { lower, upper })
        {
            anchor.Kind = RouteNodeKind.ServicePoint;
            anchor.Bindings =
            [
                new() { Role = SignalBindingRole.Fault, SignalId = $"{anchor.Id}.fault", Direction = SignalBindingDirection.Read, ValueType = SignalValueType.Bool },
                new() { Role = SignalBindingRole.ActiveRoute, SignalId = $"route.node.{anchor.Id}.active", Direction = SignalBindingDirection.Read, ValueType = SignalValueType.Bool },
            ];
        }

        var lowerLine = document.Segments.Single(line => line.Id == "lower_dead_end_to_bsu1");
        lowerLine.Kind = RouteSegmentKind.Straight;
        lowerLine.TerminalSide = null;
        lowerLine.Title = null;
        document.Segments.Single(line => line.Id == "bucket_to_upper_dead_end").ToNodeId = upper.Id;
        document.Segments.Remove(document.Segments.Single(line => line.Id == "wash_to_upper_dead_end"));
        document.Nodes.Remove(document.Nodes.Single(node => node.Id == "wash"));
        document.Chains.Single().NodeIds.Remove("wash");
        document.Chains.Single().SegmentIds.Remove("wash_to_upper_dead_end");
        return document;
    }

    [Fact]
    public async Task Storage_round_trip_preserves_version_toggles_and_styles()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Segments.Single(x => x.Id == "bsu2_to_bucket").Style.ActiveColor = "#112233";

        await scope.Storage.SaveActiveAsync(document);
        var loaded = await scope.Storage.LoadActiveAsync();

        Assert.NotNull(loaded);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, loaded.SchemaVersion);
        Assert.Equal(RouteSegmentKind.RoundedElbow90, loaded.Segments.Single(x => x.Id == "bsu2_to_bucket").Kind);
        Assert.Equal("#112233", loaded.Segments.Single(x => x.Id == "bsu2_to_bucket").Style.ActiveColor);
        Assert.Equal(RouteCommandButtonKind.Momentary, loaded.Cards.Single().StartButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, loaded.Cards.Single().StopButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, loaded.TopBar.Emergency.ButtonKind);
        Assert.Single(loaded.TopBar.Reset.Bindings, binding => binding.Role == SignalBindingRole.ResetCommand);
        Assert.False(loaded.Cards.Single().StartOffFeedbackEnabled);
        Assert.False(loaded.TopBar.Emergency.OffFeedbackEnabled);
        Assert.DoesNotContain(loaded.Cards.Single().Bindings, binding => binding.Role == SignalBindingRole.StartOffFeedback);
        Assert.DoesNotContain(loaded.TopBar.Emergency.Bindings, binding => binding.Role == SignalBindingRole.EmergencyOffFeedback);
        var json = await File.ReadAllTextAsync(scope.Path);
        Assert.Contains("\"roundedElbow90\"", json);
        Assert.Contains("\"startButtonKind\": \"momentary\"", json);
        Assert.Contains("\"buttonKind\": \"toggle\"", json);
    }

    [Fact]
    public async Task Manager_falls_back_to_seed_and_keeps_invalid_file()
    {
        using var scope = new TempConfigurationScope();
        await File.WriteAllTextAsync(scope.Path, "{ invalid json");

        using var manager = scope.CreateManager();

        Assert.NotNull(manager.LastLoadError);
        Assert.Equal(RouteMapSeed.Create().Nodes.Select(x => x.Id), manager.CurrentDefinition.Nodes.Select(x => x.Id));
        Assert.Equal("{ invalid json", await File.ReadAllTextAsync(scope.Path));
    }

    [Fact]
    public async Task Manager_migrates_standard_v1_profile_and_preserves_user_settings()
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, 1);
        document.Map.LogicalWidth = 1000;
        document.Map.Palette.Track = "#123456";
        document.Nodes.Single(x => x.Id == "dead_end_lower").X = 90;
        document.Nodes.Single(x => x.Id == "dead_end_lower").LabelOffsetX = 28;
        document.Segments.Single(x => x.Id == "bsu2_to_bucket").ArcRadius = 200;
        document.Cards.Single().Title = "Пользовательская карточка";
        await scope.Storage.SaveActiveAsync(document);

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, manager.CurrentDocument.SchemaVersion);
        Assert.Equal(1000, manager.CurrentDocument.Map.LogicalWidth);
        Assert.Equal("#123456", manager.CurrentDocument.Map.Palette.Track);
        Assert.Equal("Пользовательская карточка", manager.CurrentDocument.Cards.Single().Title);
        var lower = manager.CurrentDocument.Nodes.Single(x => x.Id == "dead_end_lower");
        Assert.Equal((240d, 438d), (lower.X, lower.Y));
        Assert.Equal((10d, 0d), (lower.LabelOffsetX, lower.LabelOffsetY));
        Assert.Equal(RouteNodeLabelPlacement.Right, lower.LabelPlacement);
        Assert.Equal(150, manager.CurrentDocument.Segments.Single(x => x.Id == "bsu2_to_bucket").ArcRadius);
        var persisted = await scope.Storage.LoadActiveAsync();
        Assert.NotNull(persisted);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.Equal(RouteCommandButtonKind.Toggle, persisted.TopBar.Emergency.ButtonKind);
        Assert.All(persisted.Cards, card =>
        {
            Assert.Equal(RouteCommandButtonKind.Momentary, card.StartButtonKind);
            Assert.Equal(RouteCommandButtonKind.Toggle, card.StopButtonKind);
            Assert.False(card.StartOffFeedbackEnabled);
            Assert.False(card.StopOffFeedbackEnabled);
            Assert.DoesNotContain(card.Bindings, binding => binding.Role == SignalBindingRole.StartOffFeedback);
            Assert.DoesNotContain(card.Bindings, binding => binding.Role == SignalBindingRole.StopOffFeedback);
        });
        Assert.DoesNotContain(persisted.TopBar.Automatic.Bindings, binding => binding.Role == SignalBindingRole.AutomaticModeOffFeedback);
        Assert.DoesNotContain(persisted.TopBar.Manual.Bindings, binding => binding.Role == SignalBindingRole.ManualModeOffFeedback);
        Assert.False(persisted.TopBar.Emergency.OffFeedbackEnabled);
        Assert.DoesNotContain(persisted.TopBar.Emergency.Bindings, binding => binding.Role == SignalBindingRole.EmergencyOffFeedback);
    }

    [Fact]
    public void Migrator_only_bumps_version_for_nonstandard_v1_map()
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, 1);
        document.Chains.Single().Id = "custom.chain";
        var lower = document.Nodes.Single(x => x.Id == "dead_end_lower");
        lower.X = 321;
        lower.Y = 654;
        lower.LabelOffsetX = 17;

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.True(result.WasMigrated);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal((321d, 654d, 17d), (lower.X, lower.Y, lower.LabelOffsetX));
        Assert.All(result.Document.Segments, segment => Assert.Single(segment.Bindings, x => x.Role == SignalBindingRole.ActiveRoute));
    }

    [Fact]
    public void Migrator_v10_to_v11_adds_empty_card_parameters()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 10;

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.True(result.WasMigrated);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.All(result.Document.Cards, card => Assert.Empty(card.Parameters));
    }

    [Fact]
    public void Migrator_v11_to_current_adds_power_feedback_and_reset_and_updates_only_legacy_disabled_color()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 11;
        document.Map.Palette.Disabled = "#D8DCDF";
        foreach (var card in document.Cards)
        {
            foreach (var binding in card.Bindings
                         .Where(x => x.Role is SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand)
                         .ToArray())
                card.Bindings.Remove(binding);
        }

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.True(result.WasMigrated);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        var reset = Assert.Single(result.Document.TopBar.Reset.Bindings, x => x.Role == SignalBindingRole.ResetCommand);
        Assert.Equal("system.reset", reset.SignalId);
        Assert.Equal("#3F474D", result.Document.Map.Palette.Disabled);
        Assert.All(result.Document.Cards, card =>
        {
            var power = Assert.Single(card.Bindings, x => x.Role == SignalBindingRole.PowerFeedback);
            Assert.Equal("equip.bucket.power", power.SignalId);
            Assert.Equal(SignalBindingDirection.Read, power.Direction);
            Assert.Equal(SignalValueType.Bool, power.ValueType);
            Assert.DoesNotContain(card.Bindings, x => x.Role is SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand);
            Assert.Equal(RouteCommandButtonKind.Momentary, card.StartButtonKind);
        });

        var custom = scope.Mapper.CreateSeedDocument();
        custom.SchemaVersion = 11;
        custom.Map.Palette.Disabled = "#123456";
        new RouteMapConfigurationMigrator().Migrate(custom);
        Assert.Equal("#123456", custom.Map.Palette.Disabled);
    }

    [Fact]
    public void Migrator_from_v16_replaces_only_bucket_selector_bindings_idempotently()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 16;
        var bucket = document.Cards.Single();
        bucket.StatusMapping = null;
        bucket.Title = "Пользовательский кюбель";
        bucket.Style.Width = 391;
        bucket.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "Уставка",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = "equip.bucket.setpoint",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Float32,
            SavedValue = "4.2",
            PendingAutoDispatch = true,
            LastDispatchError = "offline",
        });
        foreach (var binding in bucket.Bindings.Where(binding => binding.Role is SignalBindingRole.PowerFeedback or SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand).ToArray())
            bucket.Bindings.Remove(binding);
        bucket.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.UncheckedCommand,
            SignalId = "equip.bucket.selector.off",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Bool,
        });
        bucket.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.CheckedCommand,
            SignalId = "custom.selector.on",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Bool,
        });
        var customCard = scope.Storage.Clone(document).Cards.Single();
        customCard.Id = "equip.other";
        customCard.StartButtonKind = RouteCommandButtonKind.Toggle;
        customCard.AttachedChainId = null;
        customCard.Bindings.Single(binding => binding.Role == SignalBindingRole.UncheckedCommand).SignalId = "equip.other.selector.off";
        customCard.Bindings.Single(binding => binding.Role == SignalBindingRole.CheckedCommand).SignalId = "equip.other.selector.on";
        document.Cards.Add(customCard);

        var migrator = new RouteMapConfigurationMigrator();
        var migrated = migrator.Migrate(document).Document;
        var reopened = scope.Storage.Clone(migrated);
        var secondMigration = migrator.Migrate(reopened);

        Assert.False(secondMigration.WasMigrated);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        var migratedBucket = migrated.Cards.Single(card => card.Id == "equip.bucket");
        Assert.Equal("Пользовательский кюбель", migratedBucket.Title);
        Assert.Equal(391, migratedBucket.Style.Width);
        Assert.Equal("4.2", migratedBucket.Parameters.Single().SavedValue);
        Assert.True(migratedBucket.Parameters.Single().PendingAutoDispatch);
        Assert.Equal("offline", migratedBucket.Parameters.Single().LastDispatchError);
        Assert.Equal(RouteCommandButtonKind.Momentary, migratedBucket.StartButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, migratedBucket.StopButtonKind);
        var power = Assert.Single(migratedBucket.Bindings, binding => binding.Role == SignalBindingRole.PowerFeedback);
        Assert.Equal("equip.bucket.power", power.SignalId);
        Assert.Equal(SignalBindingDirection.Read, power.Direction);
        Assert.DoesNotContain(migratedBucket.Bindings, binding => binding.Role is SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand);
        var migratedCustom = migrated.Cards.Single(card => card.Id == "equip.other");
        Assert.Equal(RouteCommandButtonKind.Toggle, migratedCustom.StartButtonKind);
        Assert.DoesNotContain(migratedCustom.Bindings, binding => binding.Role == SignalBindingRole.PowerFeedback);
        Assert.Equal("equip.other.selector.on", migratedCustom.Bindings.Single(binding => binding.Role == SignalBindingRole.CheckedCommand).SignalId);
        Assert.Equal("system.mode.automatic", migrated.TopBar.Automatic.Bindings.Single(binding => binding.Role == SignalBindingRole.AutomaticModeCommand).SignalId);
    }

    [Fact]
    public async Task Manager_migrates_v17_bucket_with_custom_palette_and_preserves_the_document()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var bucket = document.Cards.Single();
        var seedMapping = bucket.StatusMapping!;
        Assert.Equal(new[] { "#77828D", "#D99B22", "#D95D4E", "#3A9D5D", "#3A9D5D", "#3A9D5D" },
            seedMapping.Entries.Select(entry => entry.Color));
        document.SchemaVersion = 17;
        bucket.StatusMapping = null;
        bucket.StatusText = "Пользовательский резерв";
        bucket.CanStart = false;
        bucket.Style.Width = 390;
        bucket.Bindings.Single(binding => binding.Role == SignalBindingRole.PowerFeedback).SignalId = "custom.power";
        bucket.Bindings.Single(binding => binding.Role == SignalBindingRole.Text).ValueType = SignalValueType.Word;
        bucket.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "Уставка",
            SignalId = "equip.bucket.setpoint",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Float32,
            SavedValue = "4.2",
            PendingAutoDispatch = true,
            LastDispatchError = "offline",
        });
        document.Map.Palette.MutedText = "#112233";
        document.Map.Palette.Warning = "#99445566";
        document.Map.Palette.Fault = "#778899";
        document.Map.Palette.Ready = "#AABBCC\n";
        var legacy = scope.Storage.Clone(document).Cards.Single();
        legacy.Id = "equip.other";
        legacy.AttachedChainId = null;
        legacy.Bindings.Single(binding => binding.Role == SignalBindingRole.Text).ValueType = SignalValueType.String;
        document.Cards.Add(legacy);
        await scope.Storage.SaveActiveAsync(document);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!;
        original["customDocumentData"] = new JsonArray(1, 2);
        original["cards"]![0]!["customCardData"] = new JsonObject { ["tag"] = "keep" };
        original["cards"]![0]!["parameters"]![0]!["vendorTag"] = new JsonObject { ["channel"] = 7 };
        original["cards"]![0]!["bindings"]![0]!["vendorTag"] = "binding";
        original["map"]!["palette"]!["vendorTag"] = "palette";
        original["segments"]![0]!["vendorTag"] = new JsonArray("segment", true);
        var originalJson = original.ToJsonString();
        await File.WriteAllTextAsync(scope.Path, originalJson);

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, manager.CurrentDocument.SchemaVersion);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(scope.Path + ".bak"));
        var mapping = manager.CurrentDefinition.MapEquipment.Single(card => card.Id == "equip.bucket").StatusMapping!;
        Assert.Equal("Неизвестный статус", mapping.UnknownText);
        Assert.Equal("Нет достоверного статуса", mapping.UnavailableText);
        Assert.Equal(new EquipmentStatusEntry[]
        {
            new(0, "Выключен", "#112233", EquipmentStatusCategory.Normal, false, false),
            new(1, "Ожидание", "#99445566", EquipmentStatusCategory.Information, false, false),
            new(2, "Авария", "#778899", EquipmentStatusCategory.Fault, true, true),
            new(3, "Выполнение", "#AABBCC", EquipmentStatusCategory.Information, false, false),
            new(4, "Выгрузка", "#AABBCC", EquipmentStatusCategory.Information, false, false),
            new(5, "Загрузка", "#AABBCC", EquipmentStatusCategory.Information, false, false),
        }, mapping.Entries);
        Assert.Null(manager.CurrentDefinition.MapEquipment.Single(card => card.Id == legacy.Id).StatusMapping);

        var persistedJson = await File.ReadAllTextAsync(scope.Path);
        var persisted = JsonNode.Parse(persistedJson)!;
        original["schemaVersion"] = RouteMapConfigurationDocument.CurrentSchemaVersion;
        original["cards"]![0]!["statusMapping"] = persisted["cards"]![0]!["statusMapping"]!.DeepClone();
        Assert.True(JsonNode.DeepEquals(original, persisted));

        using var reopened = scope.CreateManager();
        Assert.Null(reopened.LastLoadError);
        Assert.Equal(mapping.Entries, reopened.CurrentDefinition.MapEquipment[0].StatusMapping!.Entries);
        Assert.Equal(persistedJson, await File.ReadAllTextAsync(scope.Path));
        Assert.True((await reopened.SaveAndApplyAsync(reopened.CreateDraft())).IsSuccess);
        Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))));
    }

    [Fact]
    public void Migrator_preserves_an_existing_v17_table()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 17;
        var mapping = document.Cards.Single().StatusMapping!;
        mapping.Entries[0].Text = "Свой статус";
        mapping.Entries[0].Color = "#102030";

        var migrator = new RouteMapConfigurationMigrator();
        Assert.True(migrator.Migrate(document).WasMigrated);
        Assert.False(migrator.Migrate(document).WasMigrated);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, document.SchemaVersion);
        Assert.Same(mapping, document.Cards.Single().StatusMapping);
        Assert.Equal("Свой статус", mapping.Entries[0].Text);
        Assert.Equal("#102030", mapping.Entries[0].Color);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Manager_backs_up_and_upgrades_valid_v18_without_rewriting_topology_or_bindings(bool valid)
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, 18);
        var node = document.Nodes.Single(item => item.Id == "dead_end_lower");
        node.Title = "Пользовательское окончание";
        node.Y += 10;
        var line = document.Segments.Single(item => item.Id == "lower_dead_end_to_bsu1");
        line.Bindings.Single(binding => binding.Role == SignalBindingRole.ActiveRoute).SignalId = "custom.main.active";
        line.ExtensionData = new()
        {
            ["vendorTag"] = JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }),
            ["terminalCapLength"] = JsonSerializer.SerializeToElement(new { units = "vendor" }),
            ["TerminalSide"] = JsonSerializer.SerializeToElement("vendor"),
        };
        if (!valid)
            line.FromNodeId = "missing";

        await scope.Storage.SaveActiveAsync(document);
        var original = await File.ReadAllTextAsync(scope.Path);
        using var manager = scope.CreateManager();
        if (!valid)
        {
            Assert.NotNull(manager.LastLoadError);
            Assert.False(File.Exists(scope.Path + ".bak"));
            Assert.Equal(original, await File.ReadAllTextAsync(scope.Path));
            return;
        }

        Assert.Null(manager.LastLoadError);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, manager.CurrentDocument.SchemaVersion);
        var migratedLine = manager.CurrentDocument.Segments.Single(item => item.Id == line.Id);
        Assert.Null(migratedLine.TerminalCapLength);
        Assert.Equal(new[] { 1, 2, 3 }, migratedLine.ExtensionData!["vendorTag"].Deserialize<int[]>());
        Assert.Equal("vendor", migratedLine.ExtensionData["terminalCapLength"].GetProperty("units").GetString());
        Assert.Equal("vendor", migratedLine.ExtensionData["TerminalSide"].GetString());
        Assert.Equal(original, await File.ReadAllTextAsync(scope.Path + ".bak"));
        var expected = JsonNode.Parse(original)!;
        expected["schemaVersion"] = RouteMapConfigurationDocument.CurrentSchemaVersion;
        var persisted = await File.ReadAllTextAsync(scope.Path);
        Assert.True(JsonNode.DeepEquals(expected, JsonNode.Parse(persisted)));

        using var reopened = scope.CreateManager();
        Assert.Null(reopened.LastLoadError);
        Assert.Equal(persisted, await File.ReadAllTextAsync(scope.Path));
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(scope.Path)!, "route-map.json.bak*"));
    }

    [Fact]
    public void Migrator_reaches_current_version_from_all_older_versions()
    {
        using var scope = new TempConfigurationScope();
        var migrator = new RouteMapConfigurationMigrator();
        var validator = new RouteMapConfigurationValidator();
        for (var version = 1; version < RouteMapConfigurationDocument.CurrentSchemaVersion; version++)
        {
            var document = CreateLegacyTopology(scope, version);
            if (version < 18)
                document.Cards.Single().StatusMapping = null;

            Assert.True(migrator.Migrate(document).WasMigrated);
            Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, document.SchemaVersion);
            Assert.Equal(6, document.Cards.Single().StatusMapping!.Entries.Count);
            Assert.Single(document.Nodes, node => node.Id == "wash");
            Assert.Empty(validator.Validate(document).Errors);
        }
    }

    [Theory]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    public async Task Manager_upgrades_standard_topology_once_and_preserves_custom_settings(int version)
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, version);
        document.ExtensionData = new() { ["vendorRoot"] = JsonSerializer.SerializeToElement(new[] { "tag" }) };
        document.TopBar.Reset.Text = "Свой сброс";
        var card = document.Cards.Single();
        card.Title = "Свой кюбель";
        card.Parameters.Add(new()
        {
            Title = "Скорость", SignalId = "custom.speed", Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.UInt16, SavedValue = "10",
        });
        document.Chains.Add(new RouteChainConfiguration
        {
            Id = "custom.chain", NodeIds = ["bsu_1", "bsu_2"], SegmentIds = ["active_bsu1_bsu2"],
        });
        var bucketLine = document.Segments.Single(line => line.Id == "bucket_to_upper_dead_end");
        bucketLine.ActiveFragments[0].Binding.SignalId = "custom.bucket.fragment_1";
        bucketLine.Style.NormalColor = "#334455";
        bucketLine.ExtensionData = new() { ["vendorLine"] = JsonSerializer.SerializeToElement(42) };
        var lowerLine = document.Segments.Single(line => line.Id == "lower_dead_end_to_bsu1");
        lowerLine.Bindings.Single(binding => binding.Role == SignalBindingRole.Fault).SignalId = "custom.lower.fault";
        await scope.Storage.SaveActiveAsync(document);
        var original = await File.ReadAllTextAsync(scope.Path);

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.Equal(20, manager.CurrentDocument.SchemaVersion);
        Assert.True(new RouteMapConfigurationValidator().Validate(manager.CurrentDocument).IsValid);
        Assert.Equal(RouteMapSeed.Create().Chains.Single().NodeIds, manager.CurrentDefinition.Chains.Single(chain => chain.Id == "chain.bucket_route").NodeIds);
        Assert.Equal(RouteMapSeed.Create().Chains.Single().SegmentIds, manager.CurrentDefinition.Chains.Single(chain => chain.Id == "chain.bucket_route").SegmentIds);
        var updated = manager.CurrentDefinition.Segments.ToDictionary(line => line.Id);
        Assert.Equal("wash", updated[bucketLine.Id].ToNodeId);
        Assert.Equal(bucketLine.ActiveFragments.Select(fragment => fragment.Binding.SignalId),
            updated[bucketLine.Id].ActiveFragments!.Select(fragment => fragment.Binding.SignalId));
        Assert.Equal(lowerLine.Bindings.Select(binding => binding.SignalId),
            updated[lowerLine.Id].Bindings.Select(binding => binding.SignalId));
        Assert.True(updated[lowerLine.Id].LabelAtTerminal);
        var extension = updated["wash_to_upper_dead_end"];
        Assert.True(extension.LabelAtTerminal);
        Assert.Equal("#334455", extension.Style!.NormalColor);
        Assert.Empty(extension.Bindings);
        Assert.Empty(extension.ActiveFragments!);
        Assert.Equal(original, await File.ReadAllTextAsync(scope.Path + ".bak"));

        var persisted = await File.ReadAllTextAsync(scope.Path);
        var before = JsonNode.Parse(original)!;
        var after = JsonNode.Parse(persisted)!;
        foreach (var field in new[] { "map", "topBar", "cards", "placeholderRules", "vendorRoot" })
            Assert.True(JsonNode.DeepEquals(before[field], after[field]), field);
        Assert.True(JsonNode.DeepEquals(before["chains"]![1], after["chains"]![1]));
        before["segments"]![3]!["toNodeId"] = "wash";
        Assert.True(JsonNode.DeepEquals(before["segments"]![3], after["segments"]![3]));

        using var reopened = scope.CreateManager();
        Assert.Null(reopened.LastLoadError);
        Assert.Equal(persisted, await File.ReadAllTextAsync(scope.Path));
        Assert.Single(Directory.GetFiles(System.IO.Path.GetDirectoryName(scope.Path)!, "route-map.json.bak*"));
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("signal")]
    [InlineData("segment kind")]
    [InlineData("node kind")]
    public async Task Manager_keeps_custom_topology_with_standard_ids(string customization)
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, 19);
        if (customization == "endpoint")
        {
            var line = document.Segments.Single(item => item.Id == "active_bsu1_bsu2");
            (line.FromNodeId, line.ToNodeId) = (line.ToNodeId, line.FromNodeId);
        }
        else if (customization == "signal")
        {
            var bsu2 = document.Nodes.Single(node => node.Id == "bsu_2");
            bsu2.Bindings.Single(binding => binding.Role == SignalBindingRole.TargetCommand).SignalId = "ROUTE.NODE.WASH.TARGET";
        }
        else if (customization == "segment kind")
        {
            var elbow = document.Segments.Single(item => item.Id == "bsu2_to_bucket");
            elbow.Kind = RouteSegmentKind.Straight;
            RouteSegmentActiveFragmentSynchronizer.Ensure(document, elbow);
        }
        else
        {
            document.Nodes.Single(node => node.Id == "bsu_2").Kind = RouteNodeKind.ServicePoint;
        }
        await scope.Storage.SaveActiveAsync(document);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!;

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.DoesNotContain(manager.CurrentDefinition.Nodes, node => node.Id == "wash");
        original["schemaVersion"] = RouteMapConfigurationDocument.CurrentSchemaVersion;
        Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))));
    }

    [Fact]
    public async Task Manager_preserves_extension_data_and_label_position_on_existing_custom_dead_end()
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, 19);
        var (_, line) = ConfigureTerminal(document, RouteTerminalSide.From);
        line.Title = "Свой тупик";
        line.LabelOffsetX = 15;
        line.ExtensionData = new() { ["labelAtTerminal"] = JsonSerializer.SerializeToElement(new { vendor = true }) };
        await scope.Storage.SaveActiveAsync(document);
        var original = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!;

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        var model = manager.CurrentDefinition.Segments.Single(segment => segment.Id == line.Id);
        Assert.False(model.LabelAtTerminal);
        Assert.Equal(15, model.LabelOffsetX);
        original["schemaVersion"] = RouteMapConfigurationDocument.CurrentSchemaVersion;
        Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))));
    }

    [Theory]
    [InlineData(17, false)]
    [InlineData(17, true)]
    [InlineData(18, false)]
    public async Task Manager_loads_bucket_in_legacy_mode_without_a_status_mapping(int version, bool removeText)
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = version;
        var bucket = document.Cards.Single();
        bucket.StatusMapping = null;
        bucket.Title = "Пользовательская карточка";
        var text = bucket.Bindings.Single(binding => binding.Role == SignalBindingRole.Text);
        if (removeText)
            bucket.Bindings.Remove(text);
        else
            text.ValueType = SignalValueType.String;
        await scope.Storage.SaveActiveAsync(document);
        if (version == 18)
        {
            var json = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!;
            json["cards"]![0]!["statusMapping"] = null;
            await File.WriteAllTextAsync(scope.Path, json.ToJsonString());
        }

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, manager.CurrentDocument.SchemaVersion);
        var card = Assert.Single(manager.CurrentDefinition.MapEquipment);
        Assert.Equal(bucket.Title, card.Title);
        Assert.Null(card.StatusMapping);
        var mapper = new RouteMapRuntimeMapper(manager);
        var signals = new Dictionary<string, SignalValue>
        {
            [text.SignalId] = new(text.SignalId, "Пользовательский текст", SignalValueType.String, DateTimeOffset.UtcNow, true, false),
        };
        Assert.Equal(removeText ? bucket.StatusText : "Пользовательский текст", mapper.Map(signals).Find(card.Id)?.Text);
        signals[text.SignalId] = new(text.SignalId, "9", SignalValueType.String, DateTimeOffset.UtcNow, true, false);
        Assert.Equal(bucket.StatusText, mapper.Map(signals).Find(card.Id)?.Text);
    }

    [Fact]
    public async Task Status_mapping_round_trips_through_apply_save_load_export_and_import()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var draft = manager.CreateDraft();
        var mapping = draft.Cards.Single().StatusMapping!;
        mapping.UnknownText = "Свой неизвестный код";
        mapping.UnavailableText = "Нет данных";
        mapping.Entries[0].BlockStop = true;
        mapping.Entries.Add(new EquipmentStatusEntryConfiguration
        {
            Code = ushort.MaxValue,
            Text = "Особый статус",
            Color = "#80ABCDEF",
            Category = EquipmentStatusCategory.Warning,
            BlockStart = true,
        });
        var expected = scope.Mapper.ToDefinition(draft).MapEquipment.Single().StatusMapping!;

        Assert.True(manager.Validate(draft).IsSuccess);
        Assert.True(manager.Apply(draft).IsSuccess);
        Assert.False(File.Exists(scope.Path));
        Assert.Equal(expected.Entries, manager.CurrentDefinition.MapEquipment.Single().StatusMapping!.Entries);
        Assert.True((await manager.SaveAndApplyAsync(draft)).IsSuccess);
        var loaded = await manager.LoadActiveDraftAsync();
        var exportPath = scope.Path + ".export";
        Assert.True((await manager.ExportDraftAsync(exportPath, loaded)).IsSuccess);
        var imported = await manager.ImportDraftAsync(exportPath);

        var exported = JsonNode.Parse(await File.ReadAllTextAsync(exportPath))!;
        var exportedEntries = exported["cards"]![0]!["statusMapping"]!["entries"]!.AsArray();
        Assert.Equal(new[] { "normal", "information", "fault", "information", "information", "information", "warning" },
            exportedEntries.Select(entry => entry!["category"]!.GetValue<string>()));
        Assert.False(exportedEntries[0]!["blockStart"]!.GetValue<bool>());
        Assert.True(exportedEntries[0]!["blockStop"]!.GetValue<bool>());
        var domain = scope.Mapper.ToDefinition(imported);
        var reserialized = scope.Mapper.ToDocument(domain).Cards.Single().StatusMapping!;
        Assert.Equal(expected.UnknownText, reserialized.UnknownText);
        Assert.Equal(expected.UnavailableText, reserialized.UnavailableText);
        Assert.Equal(expected.Entries, domain.MapEquipment.Single().StatusMapping!.Entries);
        Assert.Equal(expected.Entries,
            reserialized.Entries.Select(entry => new EquipmentStatusEntry(
                (ushort)entry.Code, entry.Text, entry.Color, entry.Category, entry.BlockStart, entry.BlockStop)));
        mapping.Entries[0].Text = "Изменённый черновик";
        Assert.Equal("Выключен", manager.CurrentDefinition.MapEquipment.Single().StatusMapping!.Entries[0].Text);
    }

    [Fact]
    public void Validator_reports_invalid_status_mapping_fields()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        var mapping = card.StatusMapping!;
        mapping.UnknownText = " ";
        mapping.UnavailableText = new string('а', 101);
        mapping.Entries[0].Code = -1;
        mapping.Entries[1].Code = 65536;
        mapping.Entries[1].Text = string.Empty;
        mapping.Entries[2].BlockStart = false;
        mapping.Entries[3].Category = (EquipmentStatusCategory)99;
        mapping.Entries[4].Code = 3;
        mapping.Entries[4].Color = "#123456\n";
        mapping.Entries[5].Color = "red";
        mapping.Entries.Add(null!);
        var text = card.Bindings.Single(binding => binding.Role == SignalBindingRole.Text);
        var validator = new RouteMapConfigurationValidator();

        card.Bindings.Remove(text);
        var errors = validator.Validate(document).Errors;
        Assert.Equal(new[]
        {
            "StatusMapping", "StatusMapping.UnknownText", "StatusMapping.UnavailableText",
            "StatusMapping.Entries[0].Code", "StatusMapping.Entries[1].Code", "StatusMapping.Entries[1].Text",
            "StatusMapping.Entries[2]", "StatusMapping.Entries[3].Category", "StatusMapping.Entries[4].Code",
            "StatusMapping.Entries[4].Color", "StatusMapping.Entries[5].Color", "StatusMapping.Entries[6]",
        }, errors.Select(error => error.Property));
        Assert.All(errors, error => Assert.Equal(card.Id, error.ObjectId));
        card.Bindings.Add(text);
        text.ValueType = SignalValueType.String;
        Assert.Contains(validator.Validate(document).Errors, error => error.Property == "StatusMapping");
        text.ValueType = SignalValueType.Word;
        text.Direction = SignalBindingDirection.ReadWrite;
        Assert.Contains(validator.Validate(document).Errors, error => error.Property == "StatusMapping");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(256, true)]
    [InlineData(257, false)]
    public void Validator_checks_status_mapping_row_count(int count, bool valid)
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var entries = document.Cards.Single().StatusMapping!.Entries;
        entries.Clear();
        for (var code = 0; code < count; code++)
            entries.Add(new() { Code = code, Text = "Статус", Color = "#123456" });

        Assert.Equal(valid, new RouteMapConfigurationValidator().Validate(document).IsValid);
    }

    [Theory]
    [InlineData("category", "\"invalid\"")]
    [InlineData("category", "0")]
    [InlineData("code", "1.5")]
    [InlineData("code", null)]
    [InlineData("blockStop", "\"false\"")]
    public async Task Storage_rejects_invalid_status_mapping_json(string property, string? value)
    {
        using var scope = new TempConfigurationScope();
        await scope.Storage.SaveActiveAsync(scope.Mapper.CreateSeedDocument());
        var json = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!;
        var row = json["cards"]![0]!["statusMapping"]!["entries"]![0]!.AsObject();
        if (value is null)
            row.Remove(property);
        else
            row[property] = JsonNode.Parse(value);
        await File.WriteAllTextAsync(scope.Path, json.ToJsonString());

        await Assert.ThrowsAsync<JsonException>(() => scope.Storage.LoadActiveAsync());
    }

    [Fact]
    public async Task Invalid_status_mapping_cannot_publish_or_overwrite_a_profile()
    {
        using var scope = new TempConfigurationScope();
        await scope.Storage.SaveActiveAsync(scope.Mapper.CreateSeedDocument());
        var originalJson = await File.ReadAllTextAsync(scope.Path);
        using var manager = scope.CreateManager();
        var originalDefinition = manager.CurrentDefinition;
        var notifications = new List<RouteMapDefinition>();
        using var subscription = manager.DefinitionChanges.Subscribe(notifications.Add);
        var invalid = manager.CreateDraft();
        invalid.Cards.Single().StatusMapping!.Entries[2].BlockStop = false;

        Assert.False(manager.Validate(invalid).IsSuccess);
        Assert.False(manager.Apply(invalid).IsSuccess);
        Assert.False((await manager.SaveAndApplyAsync(invalid)).IsSuccess);
        Assert.False((await manager.ExportDraftAsync(scope.Path, invalid)).IsSuccess);
        Assert.Same(originalDefinition, manager.CurrentDefinition);
        Assert.Single(notifications);
        Assert.Equal(originalJson, await File.ReadAllTextAsync(scope.Path));
    }

    [Fact]
    public void Migrator_v12_to_current_adds_reset_top_bar_button()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 12;
        document.TopBar.Reset = new RouteTopBarButtonConfiguration();

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.True(result.WasMigrated);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal("СБРОС", result.Document.TopBar.Reset.Text);
        var binding = Assert.Single(result.Document.TopBar.Reset.Bindings, x => x.Role == SignalBindingRole.ResetCommand);
        Assert.Equal("system.reset", binding.SignalId);
        Assert.Equal(SignalBindingDirection.ReadWrite, binding.Direction);
        Assert.Equal(SignalValueType.Bool, binding.ValueType);
    }

    [Fact]
    public async Task Manager_migrates_v14_document_and_removes_legacy_button_color_fields()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId = "custom.auto";
        await scope.Storage.SaveActiveAsync(document);

        var json = JsonNode.Parse(await File.ReadAllTextAsync(scope.Path))!.AsObject();
        json["schemaVersion"] = 14;
        json["topBar"]!["automatic"]!["normalBackground"] = "#123456";
        json["topBar"]!["emergency"]!["checkedForeground"] = "#654321";
        json["cards"]![0]!["style"]!["startColor"] = "#ABCDEF";
        json["cards"]![0]!["style"]!["stopPressedForegroundColor"] = "#FEDCBA";
        await File.WriteAllTextAsync(scope.Path, json.ToJsonString());
        var originalJson = await File.ReadAllTextAsync(scope.Path);

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, manager.CurrentDocument.SchemaVersion);
        Assert.Equal("custom.auto", manager.CurrentDocument.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId);

        var persisted = await File.ReadAllTextAsync(scope.Path);
        Assert.False(persisted.Contains("normalBackground", StringComparison.Ordinal));
        Assert.False(persisted.Contains("checkedForeground", StringComparison.Ordinal));
        Assert.False(persisted.Contains("startColor", StringComparison.Ordinal));
        Assert.False(persisted.Contains("stopPressedForegroundColor", StringComparison.Ordinal));
        var backupPath = scope.Path + ".bak";
        Assert.True(File.Exists(backupPath));
        Assert.Equal(originalJson, await File.ReadAllTextAsync(backupPath));
        Assert.Equal(14, JsonNode.Parse(await File.ReadAllTextAsync(backupPath))!["schemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void Migrator_v15_to_v16_clears_local_setpoint_metadata()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 15;
        document.Cards.Single().Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "Скорость",
            SignalId = "equip.bucket.speed",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Float32,
            SavedValue = "1.5",
            PendingAutoDispatch = true,
            LastDispatchError = "PLC недоступен",
        });

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        var parameter = Assert.Single(result.Document.Cards.Single().Parameters);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Null(parameter.SavedValue);
        Assert.False(parameter.PendingAutoDispatch);
        Assert.Null(parameter.LastDispatchError);
    }

    [Fact]
    public async Task Parameter_value_store_persists_pending_value_and_dispatch_error_across_restart()
    {
        using var scope = new TempConfigurationScope();
        using (var manager = scope.CreateManager())
        {
            var draft = manager.CreateDraft();
            draft.Cards.Single().Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Скорость",
                SignalId = "equip.bucket.speed",
                Direction = SignalBindingDirection.ReadWrite,
                ValueType = SignalValueType.Float32,
            });
            Assert.True((await manager.SaveAndApplyAsync(draft)).IsSuccess);

            using var store = new RouteMapEquipmentParameterValueStore(manager);
            await store.SaveAsync(
                "equip.bucket",
                [new EquipmentParameterSetpoint("equip.bucket.speed", "1.5")],
                pendingAutoDispatch: true);
            Assert.Single(store.GetPending());
            await store.CompleteDispatchAsync(
                "equip.bucket",
                "equip.bucket.speed",
                "1.5",
                "PLC недоступен");
        }

        using var restarted = scope.CreateManager();
        var parameter = Assert.Single(restarted.CurrentDocument.Cards.Single().Parameters);
        Assert.Equal("1.5", parameter.SavedValue);
        Assert.False(parameter.PendingAutoDispatch);
        Assert.Equal("PLC недоступен", parameter.LastDispatchError);
    }

    [Fact]
    public void Migrator_updates_v2_visual_defaults_and_preserves_custom_active_binding()
    {
        using var scope = new TempConfigurationScope();
        var document = CreateLegacyTopology(scope, 2);
        var segment = document.Segments.Single(x => x.Id == "bsu2_to_bucket");
        segment.Style.ActiveColor = "#123456";
        var active = segment.Bindings.Single(x => x.Role == SignalBindingRole.ActiveRoute);
        active.SignalId = "custom.route.active";
        active.Direction = SignalBindingDirection.ReadWrite;
        active.ValueType = Configurator.Application.Services.Signals.SignalValueType.String;
        document.Segments.Where(x => x.Id != segment.Id).ToList().ForEach(x =>
        {
            var existing = x.Bindings.FirstOrDefault(binding => binding.Role == SignalBindingRole.ActiveRoute);
            if (existing is not null)
                x.Bindings.Remove(existing);
        });

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal("#123456", segment.Style.ActiveColor);
        Assert.Equal("custom.route.active", segment.Bindings.Single(x => x.Role == SignalBindingRole.ActiveRoute).SignalId);
        Assert.Equal(SignalBindingDirection.Read, segment.Bindings.Single(x => x.Role == SignalBindingRole.ActiveRoute).Direction);
        Assert.Equal(Configurator.Application.Services.Signals.SignalValueType.Bool, segment.Bindings.Single(x => x.Role == SignalBindingRole.ActiveRoute).ValueType);
        Assert.All(result.Document.Segments.Where(item => item.Id != "wash_to_upper_dead_end"), item =>
        {
            Assert.Equal(6, item.Style.EndpointGap);
            Assert.Equal(RouteLineCap.Round, item.Style.LineCap);
            Assert.Single(item.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
        });
    }

    [Fact]
    public async Task Invalid_migrated_profile_is_not_written_over_and_manager_uses_seed()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 1;
        document.Nodes[1].Id = document.Nodes[0].Id;
        await scope.Storage.SaveActiveAsync(document);
        var originalJson = await File.ReadAllTextAsync(scope.Path);

        using var manager = scope.CreateManager();

        Assert.NotNull(manager.LastLoadError);
        Assert.Equal(RouteMapSeed.Create().Nodes.Select(x => x.Id), manager.CurrentDefinition.Nodes.Select(x => x.Id));
        Assert.Equal(originalJson, await File.ReadAllTextAsync(scope.Path));
    }

    [Fact]
    public async Task Manager_startup_does_not_require_async_continuations_on_ui_context()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Nodes.Single(x => x.Id == "bsu_1").Title = "Загружено с диска";
        await scope.Storage.SaveActiveAsync(document);
        var previousContext = SynchronizationContext.Current;
        var rejectingContext = new RejectingSynchronizationContext();

        try
        {
            SynchronizationContext.SetSynchronizationContext(rejectingContext);
            using var manager = scope.CreateManager();

            Assert.Null(manager.LastLoadError);
            Assert.Equal("Загружено с диска", manager.CurrentDefinition.Nodes.Single(x => x.Id == "bsu_1").Title);
            Assert.Equal(0, rejectingContext.PostCount);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    [Fact]
    public void Invalid_apply_does_not_publish_or_replace_definition()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var original = manager.CurrentDefinition;
        var published = 0;
        using var subscription = manager.DefinitionChanges.Skip(1).Subscribe(_ => published++);
        var draft = manager.CreateDraft();
        draft.Nodes[1].Id = draft.Nodes[0].Id;

        var result = manager.Apply(draft);

        Assert.False(result.IsSuccess);
        Assert.Same(original, manager.CurrentDefinition);
        Assert.Equal(0, published);
    }

    [Fact]
    public void Valid_apply_publishes_one_consistent_snapshot()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var published = new List<RouteMapDefinition>();
        using var subscription = manager.DefinitionChanges.Skip(1).Subscribe(published.Add);
        var draft = manager.CreateDraft();
        draft.Nodes.Single(x => x.Id == "bsu_1").Title = "БСУ изменен";
        draft.TopBar.Automatic.Text = "АВТО";

        var result = manager.Apply(draft);

        Assert.True(result.IsSuccess);
        Assert.Single(published);
        Assert.Equal("БСУ изменен", published[0].Nodes.Single(x => x.Id == "bsu_1").Title);
        Assert.Equal("АВТО", published[0].TopBar?.Automatic.Text);
    }

    [Fact]
    public void Settings_rename_updates_references_and_delete_reports_dependencies()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        var node = viewModel.Draft.Nodes.Single(x => x.Id == "bsu_1");
        viewModel.SelectedNode = node;

        node.Id = "bsu_primary";

        Assert.Contains("bsu_primary", viewModel.Draft.Chains.Single().NodeIds);
        Assert.Contains(viewModel.Draft.Segments, x => x.FromNodeId == "bsu_primary" || x.ToNodeId == "bsu_primary");

        viewModel.DeleteNodeCommand.Execute().Subscribe();
        Assert.Contains("заблокировано", viewModel.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(viewModel.Draft.Nodes, x => x.Id == "bsu_primary");
    }

    [Fact]
    public void Settings_add_segment_creates_independent_active_route_binding()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());

        viewModel.AddSegmentCommand.Execute().Subscribe();

        var segment = viewModel.SelectedSegment!;
        var binding = Assert.Single(segment.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
        Assert.Equal($"route.{segment.Id}.active", binding.SignalId);
        Assert.Equal(SignalBindingDirection.Read, binding.Direction);
        Assert.Equal(Configurator.Application.Services.Signals.SignalValueType.Bool, binding.ValueType);
    }

    [Fact]
    public async Task Settings_dead_end_round_trips_through_export_import_save_and_reopen()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var exportPath = scope.Storage.ActiveFilePath + ".export.json";
        using var editor = new RouteMapSettingsViewModel(manager, scope.Storage, new RouteMapTestFilePicker(exportPath));
        var originalChain = editor.Draft.Chains.Single();
        var wash = editor.FilteredNodes.Single(node => node.Id == "wash");
        editor.SelectedNode = wash;

        await editor.AddDeadEndCommand.Execute().FirstAsync();
        var line = Assert.IsType<RouteSegmentConfiguration>(editor.SelectedSegment);
        var anchor = Assert.IsType<RouteNodeConfiguration>(editor.SelectedTerminalAnchor);
        Assert.Equal(RouteSegmentKind.DeadEnd, line.Kind);
        Assert.Equal(RouteTerminalSide.To, line.TerminalSide);
        Assert.Equal((wash.Id, anchor.Id), (line.FromNodeId, line.ToNodeId));
        Assert.Equal(RouteNodeKind.TerminalAnchor, anchor.Kind);
        Assert.Equal(RouteNodeMenuKind.None, anchor.MenuKind);
        Assert.False(anchor.IsLoader || anchor.IsTarget);
        Assert.Empty(anchor.Bindings);
        Assert.Empty(line.Bindings);
        Assert.Empty(line.ActiveFragments);
        Assert.DoesNotContain(anchor, editor.FilteredNodes);
        Assert.DoesNotContain(RouteNodeKind.TerminalAnchor, editor.NodeKinds);
        Assert.DoesNotContain(SignalBindingRole.ActiveRoute, editor.SegmentBindingRoles);

        editor.SelectedTerminalSide = RouteTerminalSide.From;
        line.TerminalCapLength = 42;
        line.Title = "ТУПИК ДЛЯ ПРОВЕРКИ";
        line.LabelOffsetX = 6;
        line.LabelOffsetY = 11;
        line.IsVisible = false;
        line.Style.NormalColor = "#234567";
        line.Style.Thickness = 6;
        line.Style.LineCap = RouteLineCap.Flat;
        line.Style.LabelColor = "#345678";
        line.Style.LabelFontSize = 14;
        anchor.X += 35;
        anchor.Y += 20;
        Assert.Equal((anchor.Id, wash.Id), (line.FromNodeId, line.ToNodeId));
        Assert.True(new RouteMapConfigurationValidator().Validate(editor.Draft).IsValid);
        await editor.ExportCommand.Execute().FirstAsync();
        Assert.True(File.Exists(exportPath));
        Assert.Null(editor.ErrorMessage);

        await editor.RestoreDefaultsCommand.Execute().FirstAsync();
        Assert.DoesNotContain(editor.Draft.Segments, segment => segment.Id == line.Id);
        await editor.ImportCommand.Execute().FirstAsync();
        await editor.SaveCommand.Execute().FirstAsync();
        Assert.Null(editor.ErrorMessage);

        using var reopenedManager = scope.CreateManager();
        Assert.Null(reopenedManager.LastLoadError);
        using var reopened = new RouteMapSettingsViewModel(reopenedManager, scope.Storage, new NullFilePicker());
        reopened.SelectedSegment = reopened.Draft.Segments.Single(segment => segment.Id == line.Id);
        var saved = reopened.SelectedSegment;
        Assert.Equal((line.TerminalSide, line.TerminalCapLength, line.Title), (saved.TerminalSide, saved.TerminalCapLength, saved.Title));
        Assert.Equal((6d, 11d, false), (saved.LabelOffsetX, saved.LabelOffsetY, saved.IsVisible));
        Assert.Equal(("#234567", 6d, RouteLineCap.Flat, "#345678", 14d),
            (saved.Style.NormalColor, saved.Style.Thickness, saved.Style.LineCap, saved.Style.LabelColor, saved.Style.LabelFontSize));
        Assert.Equal((anchor.X, anchor.Y), (reopened.SelectedTerminalAnchor!.X, reopened.SelectedTerminalAnchor.Y));
        Assert.Equal(originalChain.NodeIds.ToArray(), reopened.Draft.Chains.Single().NodeIds.ToArray());
        Assert.Equal(originalChain.SegmentIds.ToArray(), reopened.Draft.Chains.Single().SegmentIds.ToArray());
        var standardNode = reopened.Draft.Nodes.Single(node => node.Id == "bsu_1").Style;
        var standardLine = reopened.Draft.Segments.Single(segment => segment.Id == "wash_to_upper_dead_end").Style;
        Assert.Equal(("#68717A", 0d, "#68717A", 13d),
            (standardNode.FillColor, standardNode.InnerRadiusRatio, standardNode.LabelColor, standardNode.LabelFontSize));
        Assert.Equal(("#68717A", "#68717A", 13d), (standardLine.NormalColor, standardLine.LabelColor, standardLine.LabelFontSize));
        Assert.Empty(saved.Bindings);
        saved.TerminalCapLength = 8;
        await reopened.CloseCommand.Execute().FirstAsync();
        Assert.Equal(42, (await reopenedManager.LoadActiveDraftAsync()).Segments.Single(segment => segment.Id == line.Id).TerminalCapLength);
    }

    [Fact]
    public async Task Settings_duplicate_and_delete_dead_end_protect_chain_and_anchor_references()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var editor = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        var source = editor.Draft.Segments.Single(segment => segment.Id == "lower_dead_end_to_bsu1");
        source.TerminalCapLength = 38;
        editor.SelectedSegment = source;
        var sourceAnchor = editor.SelectedTerminalAnchor!;
        var signalIds = source.Bindings.Select(binding => binding.SignalId).ToArray();

        await editor.DeleteSegmentCommand.Execute().FirstAsync();
        Assert.Contains("chain.bucket_route", editor.ErrorMessage);
        Assert.Contains(source, editor.Draft.Segments);
        await editor.DuplicateSegmentCommand.Execute().FirstAsync();
        var copy = editor.SelectedSegment!;
        var copyAnchor = editor.SelectedTerminalAnchor!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.NotEqual(sourceAnchor.Id, copyAnchor.Id);
        Assert.Equal((source.TerminalSide, source.TerminalCapLength), (copy.TerminalSide, copy.TerminalCapLength));
        Assert.Empty(copy.Bindings);
        Assert.Empty(copyAnchor.Bindings);
        Assert.Equal(signalIds, source.Bindings.Select(binding => binding.SignalId));
        Assert.Equal((sourceAnchor.X, sourceAnchor.Y), (copyAnchor.X, copyAnchor.Y));
        copyAnchor.X += 10;
        copy.Style.NormalColor = "#012345";
        Assert.NotEqual(source.Style.NormalColor, copy.Style.NormalColor);
        Assert.NotEqual(sourceAnchor.X, copyAnchor.X);
        Assert.True(new RouteMapConfigurationValidator().Validate(editor.Draft).IsValid);

        var chain = editor.Draft.Chains.Single();
        chain.NodeIds.Add(copyAnchor.Id);
        await editor.DeleteSegmentCommand.Execute().FirstAsync();
        Assert.Contains(copy, editor.Draft.Segments);
        Assert.Contains(copyAnchor, editor.Draft.Nodes);
        chain.NodeIds.Remove(copyAnchor.Id);
        var unaffectedNodes = editor.Draft.Nodes.Where(node => node != copyAnchor).ToArray();
        var unaffectedSegments = editor.Draft.Segments.Where(segment => segment != copy).ToArray();
        await editor.DeleteSegmentCommand.Execute().FirstAsync();
        Assert.Equal(unaffectedNodes, editor.Draft.Nodes);
        Assert.Equal(unaffectedSegments, editor.Draft.Segments);
        Assert.True(new RouteMapConfigurationValidator().Validate(editor.Draft).IsValid);
    }

    [Fact]
    public async Task Settings_kind_switch_preserves_endpoints_arc_parameters_and_signal_ids()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var editor = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        editor.SelectedSegment = editor.Draft.Segments.Single(segment => segment.Id == "bsu2_to_bucket");
        await editor.DuplicateSegmentCommand.Execute().FirstAsync();
        var line = editor.SelectedSegment!;
        var endpoints = (line.FromNodeId, line.ToNodeId);
        var fragments = line.ActiveFragments.Select(fragment => fragment.Binding.SignalId).ToArray();
        var mainBinding = line.Bindings.Single(binding => binding.Role == SignalBindingRole.ActiveRoute).SignalId;
        var radius = line.ArcRadius;

        editor.SelectedSegmentKind = RouteSegmentKind.DeadEnd;
        Assert.NotNull(editor.SelectedTerminalAnchor);
        Assert.Equal(fragments, line.ActiveFragments.Select(fragment => fragment.Binding.SignalId));
        line.TerminalCapLength = 32;
        editor.SelectedSegmentKind = RouteSegmentKind.RoundedElbow90;
        Assert.Equal(endpoints, (line.FromNodeId, line.ToNodeId));
        Assert.Null(editor.SelectedTerminalAnchor);
        Assert.Equal(radius, line.ArcRadius);
        Assert.Equal(32, line.TerminalCapLength);
        Assert.Equal(mainBinding, line.Bindings.Single(binding => binding.Role == SignalBindingRole.ActiveRoute).SignalId);
        await editor.SaveCommand.Execute().FirstAsync();
        Assert.Null(editor.ErrorMessage);
        Assert.Equal(fragments, manager.CurrentDocument.Segments.Single(segment => segment.Id == line.Id)
            .ActiveFragments.Select(fragment => fragment.Binding.SignalId));
    }

    private sealed class RouteMapTestFilePicker(string path) : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(path);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(path);
    }

    [Fact]
    public void Settings_add_node_and_card_create_required_command_bindings()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());

        viewModel.AddNodeCommand.Execute().Subscribe();
        viewModel.SelectedNode!.MenuKind = RouteNodeMenuKind.SendAndReturn;
        viewModel.AddCardCommand.Execute().Subscribe();
        viewModel.ApplyCommand.Execute().Subscribe();

        Assert.Single(viewModel.SelectedNode.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
        Assert.Single(viewModel.SelectedNode.Bindings, x => x.Role == SignalBindingRole.TargetCommand);
        Assert.Single(viewModel.SelectedNode.Bindings, x => x.Role == SignalBindingRole.LoaderCommand);
        Assert.DoesNotContain(viewModel.SelectedNode.Bindings, x => x.Role == SignalBindingRole.TargetOffFeedback);
        Assert.DoesNotContain(viewModel.SelectedNode.Bindings, x => x.Role == SignalBindingRole.LoaderOffFeedback);
        Assert.Single(viewModel.SelectedCard!.Bindings, x => x.Role == SignalBindingRole.StartCommand);
        Assert.DoesNotContain(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        Assert.Single(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.StopCommand);
        Assert.DoesNotContain(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);
        Assert.Single(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.PowerFeedback);
        Assert.DoesNotContain(viewModel.SelectedCard.Bindings, x => x.Role is SignalBindingRole.UncheckedCommand or SignalBindingRole.CheckedCommand);
        Assert.Equal(RouteCommandButtonKind.Momentary, viewModel.SelectedCard.StartButtonKind);
        Assert.Contains(SignalBindingRole.PowerFeedback, viewModel.CardBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.NodeBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.SegmentBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.CardBindingRoles);
        Assert.Empty(viewModel.AutomaticModeBindingRoles);
        Assert.Empty(viewModel.ManualModeBindingRoles);
        Assert.Contains(SignalBindingRole.ResetCommand, viewModel.ResetBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.ResetBindingRoles);
    }

    [Fact]
    public void Mapper_preserves_top_bar_enabled_bindings()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId = "custom.auto";
        document.TopBar.Manual.Bindings.Single(x => x.Role == SignalBindingRole.ManualModeCommand).SignalId = "custom.manual";
        document.TopBar.Automatic.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.Enabled,
            SignalId = "system.mode.automatic.enabled",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool,
        });

        var definition = scope.Mapper.ToDefinition(document);
        var roundTrip = scope.Mapper.ToDocument(definition);

        Assert.Equal("system.mode.automatic.enabled", definition.TopBar!.Automatic.EnabledBinding?.SignalId);
        Assert.Contains(roundTrip.TopBar.Automatic.Bindings, x => x.Role == SignalBindingRole.Enabled);
        Assert.Equal("custom.auto", roundTrip.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId);
        Assert.Equal("custom.manual", roundTrip.TopBar.Manual.Bindings.Single(x => x.Role == SignalBindingRole.ManualModeCommand).SignalId);
    }

    [Fact]
    public void Settings_status_table_toggle_and_card_selection_only_change_the_draft()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var published = manager.CurrentDefinition;
        using (var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker()))
        {
            var bucket = viewModel.SelectedCard!;
            var bindings = bucket.Bindings.ToArray();
            bucket.StatusMapping!.Entries[0].Text = "Черновик";
            bucket.UseStatusMapping = false;
            Assert.Null(bucket.StatusMapping);
            bucket.UseStatusMapping = true;
            Assert.Equal(Enumerable.Range(0, 6), bucket.StatusMapping!.Entries.Select(entry => entry.Code));
            Assert.Equal("Выключен", bucket.StatusMapping.Entries[0].Text);
            Assert.Equal(bindings, bucket.Bindings);
            Assert.Equal("Неизвестный статус", bucket.StatusMapping.UnknownText);
            Assert.Equal("Нет достоверного статуса", bucket.StatusMapping.UnavailableText);

            viewModel.AddCardCommand.Execute().Subscribe();
            var custom = viewModel.SelectedCard!;
            Assert.Null(custom.StatusMapping);
            var customBindings = custom.Bindings.ToArray();
            custom.UseStatusMapping = true;
            var mapping = custom.StatusMapping!;
            Assert.Empty(mapping.Entries);
            Assert.Contains("от 1 до 256", mapping.ValidationMessage);
            Assert.Contains("binding Text", mapping.ValidationMessage);
            Assert.Equal(customBindings, custom.Bindings);

            viewModel.AddStatusCommand.Execute().Subscribe();
            var entry = Assert.Single(mapping.Entries);
            entry.CodeInput = "65535";
            entry.Text = "Состояние линии";
            entry.Color = "#80123456";
            entry.EditorCategory = EquipmentStatusCategory.Warning;
            entry.BlockStart = true;
            Assert.False(entry.BlockStop);
            entry.BlockStop = true;
            entry.BlockStart = false;
            Assert.True(entry.BlockStop);
            entry.EditorCategory = EquipmentStatusCategory.Fault;
            Assert.True(entry.BlockStart);
            Assert.True(entry.BlockStop);
            Assert.False(entry.CanEditBlocks);
            Assert.Equal(65535, entry.Code);

            viewModel.SelectedCard = bucket;
            viewModel.SelectedCard = custom;
            Assert.Same(entry, Assert.Single(custom.StatusMapping!.Entries));
            Assert.Equal("Состояние линии", entry.Text);
            Assert.Equal("#80123456", entry.Color);
            viewModel.AddStatusCommand.Execute().Subscribe();
            Assert.Equal(0, mapping.Entries[1].Code);
            viewModel.RemoveStatusCommand.Execute(entry).Subscribe();
            Assert.Equal(0, Assert.Single(mapping.Entries).Code);
            Assert.Equal(6, bucket.StatusMapping.Entries.Count);
            Assert.Same(published, manager.CurrentDefinition);
            Assert.False(File.Exists(scope.Path));
        }

        using var reopened = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        Assert.Equal("Выключен", Assert.Single(reopened.Draft.Cards).StatusMapping!.Entries[0].Text);
        Assert.Same(published, manager.CurrentDefinition);
    }

    [Fact]
    public async Task Settings_invalid_status_edits_block_apply_and_save_and_show_inline_errors()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var published = manager.CurrentDefinition;
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        var card = viewModel.SelectedCard!;
        var mapping = card.StatusMapping!;
        var binding = card.Bindings.Single(binding => binding.Role == SignalBindingRole.Text);
        binding.ValueType = SignalValueType.String;
        mapping.UnknownText = new string('я', 101);
        mapping.UnavailableText = " ";
        mapping.Entries[1].CodeInput = "0";
        mapping.Entries[1].Color = "#broken";
        mapping.Entries[1].Text = " ";
        mapping.Entries[2].BlockStop = false;

        Assert.Contains("binding Text", mapping.ValidationMessage);
        Assert.Contains("Текст неизвестного статуса", mapping.ValidationMessage);
        Assert.Contains("Текст недостоверного статуса", mapping.ValidationMessage);
        Assert.Contains("уже указан", mapping.Entries[1].ValidationMessage);
        Assert.Contains("#RRGGBB", mapping.Entries[1].ValidationMessage);
        Assert.Contains("от 1 до 100", mapping.Entries[1].ValidationMessage);
        Assert.Contains("должен блокировать", mapping.Entries[2].ValidationMessage);
        viewModel.Apply();
        Assert.Contains("уже указан", viewModel.ErrorMessage);
        await viewModel.SaveAsync();
        Assert.Contains("уже указан", viewModel.ErrorMessage);
        Assert.Same(published, manager.CurrentDefinition);
        Assert.False(File.Exists(scope.Path));

        binding.ValueType = SignalValueType.Word;
        mapping.UnknownText = "Неизвестен";
        mapping.UnavailableText = "Нет данных";
        mapping.Entries[1].Color = "#FFABCDEF";
        mapping.Entries[1].Text = "Ожидание";
        mapping.Entries[2].EditorCategory = EquipmentStatusCategory.Fault;
        Assert.Empty(mapping.ValidationMessage!);
        foreach (var invalidCode in new[] { "", "1.2", "65536", "слово", "-1", "99999999999" })
        {
            mapping.Entries[1].CodeInput = invalidCode;
            Assert.Equal(invalidCode, mapping.Entries[1].CodeInput);
            Assert.Contains("0–65535", mapping.Entries[1].ValidationMessage);
            Assert.False(manager.Validate(viewModel.Draft).IsSuccess);
        }
        mapping.Entries[1].CodeInput = "1";
        Assert.Empty(mapping.Entries[1].ValidationMessage!);
        Assert.True(manager.Validate(viewModel.Draft).IsSuccess);

        viewModel.AddCardCommand.Execute().Subscribe();
        var custom = viewModel.SelectedCard!;
        custom.UseStatusMapping = true;
        viewModel.Apply();
        Assert.Contains("от 1 до 256", viewModel.ErrorMessage);
        Assert.Contains("binding Text", viewModel.ErrorMessage);
        viewModel.AddStatusCommand.Execute().Subscribe();
        custom.StatusMapping!.Entries[0].Text = "Собственный статус";
        viewModel.Apply();
        Assert.Contains("binding Text", viewModel.ErrorMessage);
        Assert.Same(published, manager.CurrentDefinition);

        custom.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.Text,
            SignalId = $"{custom.Id}.text",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.UInt16,
        });
        viewModel.Apply();
        Assert.Null(viewModel.ErrorMessage);
        Assert.Equal("Собственный статус", manager.CurrentDefinition.MapEquipment.Single(item => item.Id == custom.Id)
            .StatusMapping!.Entries[0].Text);
        Assert.Equal("Выключен", manager.CurrentDefinition.MapEquipment.Single(item => item.Id == card.Id)
            .StatusMapping!.Entries[0].Text);
    }

    [Fact]
    public async Task Settings_import_v17_edit_apply_save_and_export_current_preserve_legacy_and_custom_data()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var source = manager.CreateDraft();
        source.SchemaVersion = 17;
        source.Cards[0].StatusMapping = null;
        source.Cards[0].StatusText = "Резервный статус";
        source.Cards[0].ExtensionData = new() { ["custom"] = JsonSerializer.SerializeToElement("keep") };
        var legacy = scope.Storage.Clone(source).Cards[0];
        legacy.Id = "equip.legacy";
        legacy.AttachedChainId = null;
        legacy.StatusText = "Резерв legacy";
        source.Cards.Add(legacy);
        var inputPath = scope.Path + ".v17";
        var outputPath = scope.Path + ".export";
        await scope.Storage.SaveAsync(inputPath, source);
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new StatusTestFilePicker(inputPath, outputPath));
        var published = manager.CurrentDefinition;
        await viewModel.ImportCommand.Execute().FirstAsync();
        Assert.Same(published, manager.CurrentDefinition);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, viewModel.Draft.SchemaVersion);
        var mapping = viewModel.SelectedCard!.StatusMapping!;
        Assert.Equal(6, mapping.Entries.Count);
        Assert.Null(viewModel.Draft.Cards[1].StatusMapping);

        var entry = mapping.Entries[0];
        entry.CodeInput = "42";
        entry.Text = " Готово ✓ ";
        entry.Color = "#FF126534";
        entry.EditorCategory = EquipmentStatusCategory.Warning;
        entry.BlockStop = true;
        mapping.UnknownText = "Состояние неизвестно";
        mapping.UnavailableText = "Состояние недоступно";
        Assert.Same(published, manager.CurrentDefinition);
        Assert.False(File.Exists(scope.Path));
        viewModel.Apply();
        Assert.Null(viewModel.ErrorMessage);
        Assert.False(File.Exists(scope.Path));
        var definition = manager.CurrentDefinition;
        var configured = definition.MapEquipment[0].StatusMapping!;
        Assert.Equal(new EquipmentStatusEntry(42, " Готово ✓ ", "#FF126534", EquipmentStatusCategory.Warning, false, true),
            configured.Entries[0]);
        Assert.Equal("Резерв legacy", definition.MapEquipment[1].StatusText);
        Assert.Null(definition.MapEquipment[1].StatusMapping);

        await viewModel.SaveAsync();
        Assert.Null(viewModel.ErrorMessage);
        using var reopened = scope.CreateManager();
        Assert.Null(reopened.LastLoadError);
        Assert.Equal(configured.Entries, reopened.CurrentDefinition.MapEquipment[0].StatusMapping!.Entries);
        Assert.Equal(configured.UnknownText, reopened.CurrentDefinition.MapEquipment[0].StatusMapping!.UnknownText);
        Assert.Equal(configured.UnavailableText, reopened.CurrentDefinition.MapEquipment[0].StatusMapping!.UnavailableText);
        Assert.Equal("keep", reopened.CurrentDocument.Cards[0].ExtensionData!["custom"].GetString());

        await viewModel.ExportCommand.Execute().FirstAsync();
        Assert.Null(viewModel.ErrorMessage);
        var exported = await manager.ImportDraftAsync(outputPath);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, exported.SchemaVersion);
        Assert.Equal("Резервный статус", exported.Cards[0].StatusText);
        Assert.Equal(" Готово ✓ ", exported.Cards[0].StatusMapping!.Entries[0].Text);
        Assert.Null(exported.Cards[1].StatusMapping);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(await File.ReadAllTextAsync(scope.Path)),
            JsonNode.Parse(await File.ReadAllTextAsync(outputPath))));

        viewModel.SelectedCard!.UseStatusMapping = false;
        Assert.NotNull(manager.CurrentDefinition.MapEquipment[0].StatusMapping);
        viewModel.Apply();
        Assert.Null(manager.CurrentDefinition.MapEquipment[0].StatusMapping);
        Assert.NotNull((await scope.Storage.LoadActiveAsync())!.Cards[0].StatusMapping);
    }

    [Fact]
    public void Settings_duplicate_card_rewrites_power_signal_id_and_clones_status_mapping()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        var source = viewModel.SelectedCard!;
        var sourceMapping = source.StatusMapping!;
        sourceMapping.Entries[1].CodeInput = "1.5";

        viewModel.DuplicateCardCommand.Execute().Subscribe();

        var card = viewModel.SelectedCard!;
        Assert.Equal($"{card.Id}.power", card.Bindings.Single(x => x.Role == SignalBindingRole.PowerFeedback).SignalId);
        Assert.Equal(source.Bindings.Single(x => x.Role == SignalBindingRole.Text).SignalId,
            card.Bindings.Single(x => x.Role == SignalBindingRole.Text).SignalId);
        var mapping = card.StatusMapping!;
        Assert.NotSame(sourceMapping, mapping);
        Assert.NotSame(sourceMapping.Entries, mapping.Entries);
        Assert.Equal(sourceMapping.Entries.Select(row => (row.Code, row.Color)), mapping.Entries.Select(row => (row.Code, row.Color)));
        Assert.NotSame(sourceMapping.Entries[0], mapping.Entries[0]);
        Assert.Equal("1.5", mapping.Entries[1].CodeInput);
        mapping.Entries[0].Color = "#AABBCC";
        Assert.Equal("#77828D", sourceMapping.Entries[0].Color);
    }

    [Fact]
    public void Settings_rejects_mode_bindings_and_keeps_reset_enabled_binding_editable()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());

        viewModel.AddBindingCommand.Execute("automatic").Subscribe();
        viewModel.AddBindingCommand.Execute("manual").Subscribe();
        Assert.DoesNotContain(viewModel.Draft.TopBar.Automatic.Bindings, x => x.Role == SignalBindingRole.Enabled);
        Assert.DoesNotContain(viewModel.Draft.TopBar.Manual.Bindings, x => x.Role == SignalBindingRole.Enabled);

        viewModel.AddBindingCommand.Execute("reset").Subscribe();
        var resetEnabled = Assert.Single(viewModel.Draft.TopBar.Reset.Bindings, x => x.Role == SignalBindingRole.Enabled);
        Assert.Equal("system.reset.enabled", resetEnabled.SignalId);

        viewModel.RemoveBindingCommand.Execute(resetEnabled).Subscribe();
        Assert.DoesNotContain(viewModel.Draft.TopBar.Reset.Bindings, x => x.Role == SignalBindingRole.Enabled);
    }

    [Fact]
    public void Settings_removes_card_parameter_from_draft_and_published_signal_inventory()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());
        var card = viewModel.SelectedCard!;
        var parameter = new EquipmentCardParameterConfiguration
        {
            Title = "Скорость",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = "equip.bucket.speed",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Word
        };
        card.Parameters.Add(parameter);

        viewModel.RemoveCardParameterCommand.Execute(parameter).Subscribe();
        viewModel.ApplyCommand.Execute().Subscribe();

        Assert.DoesNotContain(card.Parameters, item => item.SignalId == "equip.bucket.speed");
        Assert.DoesNotContain(
            RouteMapSignalInventory.Build(manager.CurrentDefinition),
            item => item.SignalId == "equip.bucket.speed");
    }

    [Fact]
    public void Mapper_preserves_card_off_feedback_bindings()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        card.StartOffFeedbackEnabled = true;
        card.StopOffFeedbackEnabled = true;
        card.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.StartOffFeedback,
            SignalId = "equip.bucket.start.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });
        card.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.StopOffFeedback,
            SignalId = "equip.bucket.stop.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });

        var definition = scope.Mapper.ToDefinition(document);
        var roundTrip = scope.Mapper.ToDocument(definition);

        var modelCard = definition.MapEquipment.Single();
        Assert.True(modelCard.StartOffFeedbackEnabled);
        Assert.True(modelCard.StopOffFeedbackEnabled);
        Assert.Contains(modelCard.Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        Assert.Contains(modelCard.Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);
        Assert.Contains(roundTrip.Cards.Single().Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        Assert.Contains(roundTrip.Cards.Single().Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);
    }

    [Fact]
    public void Mapper_round_trip_preserves_card_parameters()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "Скорость",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = "equip.bucket.speed",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Float32,
            SavedValue = "1.25",
            PendingAutoDispatch = true,
            LastDispatchError = "PLC недоступен",
        });
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "Дата",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = "equip.bucket.date",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Date,
        });
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "DWORD",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = "equip.bucket.dword",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Dword,
        });
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "WORD",
            Role = SignalBindingRole.EquipmentParameter,
            SignalId = "equip.bucket.word",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.Word,
        });

        var definition = scope.Mapper.ToDefinition(document);
        var roundTrip = scope.Mapper.ToDocument(definition);

        var modelParameter = definition.MapEquipment.Single().Parameters.Single(x => x.Binding.SignalId == "equip.bucket.speed");
        Assert.Equal("Скорость", modelParameter.Title);
        Assert.Equal(new SignalBinding(
            SignalBindingRole.EquipmentParameter,
            "equip.bucket.speed",
            SignalBindingDirection.ReadWrite,
            SignalValueType.Float32), modelParameter.Binding);
        Assert.Contains(definition.MapEquipment.Single().Parameters, x => x.Binding.ValueType == SignalValueType.Date);
        Assert.Contains(definition.MapEquipment.Single().Parameters, x => x.Binding.ValueType == SignalValueType.Dword);
        Assert.Contains(definition.MapEquipment.Single().Parameters, x => x.Binding.ValueType == SignalValueType.Word);
        var configurationParameter = roundTrip.Cards.Single().Parameters.Single(x => x.SignalId == "equip.bucket.speed");
        Assert.Equal("Скорость", configurationParameter.Title);
        Assert.Equal(SignalBindingRole.EquipmentParameter, configurationParameter.Role);
        Assert.Equal("equip.bucket.speed", configurationParameter.SignalId);
        Assert.Equal("1.25", modelParameter.SavedValue);
        Assert.True(modelParameter.PendingAutoDispatch);
        Assert.Equal("PLC недоступен", modelParameter.LastDispatchError);
        Assert.Equal("1.25", configurationParameter.SavedValue);
        Assert.True(configurationParameter.PendingAutoDispatch);
        Assert.Equal("PLC недоступен", configurationParameter.LastDispatchError);
        Assert.Contains(roundTrip.Cards.Single().Parameters, x => x.ValueType == SignalValueType.Date);
        Assert.Contains(roundTrip.Cards.Single().Parameters, x => x.ValueType == SignalValueType.Dword);
        Assert.Contains(roundTrip.Cards.Single().Parameters, x => x.ValueType == SignalValueType.Word);
    }

    [Fact]
    public void Validator_rejects_missing_required_node_card_and_top_bar_bindings()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var wash = document.Nodes.Single(node => node.Id == "wash");
        wash.Bindings.Remove(wash.Bindings.Single(x => x.Role == SignalBindingRole.ActiveRoute));
        var card = document.Cards.Single();
        card.Bindings.Remove(card.Bindings.Single(x => x.Role == SignalBindingRole.StartCommand));
        document.TopBar.Automatic.Bindings.Clear();

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Scope == "node" && x.Message.Contains("ActiveRoute"));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Message.Contains("StartCommand"));
        Assert.DoesNotContain(result.Errors, x => x.Scope == "topBar" && x.Message.Contains("AutomaticModeCommand"));
    }

    [Fact]
    public void Validator_rejects_invalid_card_parameters()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = string.Empty,
            Role = SignalBindingRole.StartCommand,
            SignalId = string.Empty,
            Direction = (SignalBindingDirection)999,
            ValueType = (SignalValueType)999,
        });
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "A",
            SignalId = "equip.bucket.duplicate",
        });
        card.Parameters.Add(new EquipmentCardParameterConfiguration
        {
            Title = "B",
            SignalId = "equip.bucket.duplicate",
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Property == nameof(EquipmentCardParameterConfiguration.Title));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Property == nameof(EquipmentCardParameterConfiguration.SignalId));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Property == nameof(EquipmentCardParameterConfiguration.Role));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Property == nameof(EquipmentCardParameterConfiguration.Direction));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Property == nameof(EquipmentCardParameterConfiguration.ValueType));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Message.Contains("duplicate"));
    }

    [Fact]
    public void Validator_accepts_card_off_feedback_and_rejects_emergency_off_feedback_role()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        card.StartOffFeedbackEnabled = true;
        card.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.StartOffFeedback,
            SignalId = "equip.bucket.start.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });
        document.TopBar.Emergency.OffFeedbackEnabled = true;
        document.TopBar.Emergency.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.EmergencyOffFeedback,
            SignalId = "system.emergency.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Property == nameof(document.TopBar.Emergency.OffFeedbackEnabled));
        Assert.Contains(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.EmergencyOffFeedback)));
        Assert.DoesNotContain(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.StartOffFeedback)));
    }

    [Fact]
    public void Validator_accepts_bucket_momentary_start_and_rejects_momentary_emergency()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Cards.Single().StartButtonKind = RouteCommandButtonKind.Momentary;
        document.Cards.Single().StopButtonKind = RouteCommandButtonKind.Toggle;
        document.TopBar.Emergency.ButtonKind = RouteCommandButtonKind.Momentary;

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.DoesNotContain(result.Errors, x => x.Property == nameof(EquipmentCardConfiguration.StartButtonKind));
        Assert.Contains(result.Errors, x => x.Property == nameof(RouteTopBarEmergencyButtonConfiguration.ButtonKind));
    }

    [Fact]
    public async Task Top_bar_settings_command_opens_dialog_service()
    {
        var service = new RecordingSettingsDialogService();
        var viewModel = new TopBarViewModel(service);

        await viewModel.OpenSettingsCommand.Execute().FirstAsync();

        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public async Task Top_bar_help_command_opens_dialog_service()
    {
        var service = new RecordingHelpDialogService();
        var viewModel = new TopBarViewModel(helpDialogService: service);

        await viewModel.OpenHelpCommand.Execute().FirstAsync();

        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public void Top_bar_reset_and_emergency_colors_resolve_pressed_checked_normal_priority()
    {
        var viewModel = new TopBarViewModel(settingsDialogService: null, settings: RouteMapSeed.Create().TopBar);

        Assert.False(viewModel.IsAutomaticCommandEnabled);
        Assert.False(viewModel.IsManualCommandEnabled);
        Assert.Equal(Color.Parse("#ECEFF1"), BrushColor(viewModel.ResetBackground));
        Assert.Equal(Color.Parse("#FF2626"), BrushColor(viewModel.EmergencyBackground));
        Assert.Equal(Color.Parse("#101820"), BrushColor(viewModel.EmergencyForeground));

        viewModel.ApplyRuntime(
            isAutomaticMode: true,
            isManualMode: false,
            isResetActive: true,
            hasEmergency: true,
            isConnectionAvailable: true);

        Assert.Equal(Color.Parse("#D1C300"), BrushColor(viewModel.ResetBackground));
        Assert.Equal(Color.Parse("#D10000"), BrushColor(viewModel.EmergencyBackground));

        viewModel.SetResetPressed(true);
        Assert.Equal(Color.Parse("#D1C300"), BrushColor(viewModel.ResetBackground));

        viewModel.SetEmergencyPressed(true);

        Assert.Equal(Color.Parse("#D10000"), BrushColor(viewModel.EmergencyBackground));
        Assert.Equal(Color.Parse("#FFFFFF"), BrushColor(viewModel.EmergencyForeground));

        viewModel.SetEmergencyPressed(false);
        viewModel.SetEmergencyHovered(true);

        Assert.Equal(Color.Parse("#D10000"), BrushColor(viewModel.EmergencyBackground));
    }

    [Fact]
    public void Top_bar_hover_colors_apply_when_button_is_not_pressed_or_checked()
    {
        var viewModel = new TopBarViewModel(settings: RouteMapSeed.Create().TopBar);

        viewModel.SetResetHovered(true);
        Assert.Equal(Color.Parse("#FFF78A"), BrushColor(viewModel.ResetBackground));

        viewModel.SetResetPressed(true);
        Assert.Equal(Color.Parse("#D1C300"), BrushColor(viewModel.ResetBackground));
        Assert.Equal(Color.Parse("#FFFFFF"), BrushColor(viewModel.ResetForeground));

        viewModel.SetEmergencyHovered(true);
        Assert.Equal(Color.Parse("#FF8A8A"), BrushColor(viewModel.EmergencyBackground));
        Assert.Equal(Color.Parse("#101820"), BrushColor(viewModel.EmergencyForeground));

        viewModel.SetEmergencyPressed(true);
        Assert.Equal(Color.Parse("#D10000"), BrushColor(viewModel.EmergencyBackground));
    }

    [Fact]
    public void Migrator_adds_v4_node_and_top_bar_bindings_without_replacing_custom_signal_ids()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 3;
        document.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId = "custom.auto";
        foreach (var node in document.Nodes)
        {
            node.Bindings = new System.Collections.ObjectModel.ObservableCollection<SignalBindingConfiguration>(
                node.Bindings.Where(x => x.Role is not SignalBindingRole.ActiveRoute
                    and not SignalBindingRole.TargetCommand
                    and not SignalBindingRole.LoaderCommand));
        }

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal("custom.auto", result.Document.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId);
        Assert.DoesNotContain(result.Document.TopBar.Automatic.Bindings, x => x.Role == SignalBindingRole.AutomaticModeOffFeedback);
        Assert.DoesNotContain(result.Document.TopBar.Manual.Bindings, x => x.Role == SignalBindingRole.ManualModeOffFeedback);
        Assert.All(result.Document.Nodes, node =>
        {
            var active = Assert.Single(node.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
            Assert.Equal($"route.node.{node.Id}.active", active.SignalId);
            Assert.Equal("#00A6A6", node.Style.ActiveOutlineColor);
            Assert.Equal(3, node.Style.ActiveOutlineThickness);
        });
        Assert.Single(result.Document.Nodes.Single(x => x.Id == "bsu_1").Bindings, x => x.Role == SignalBindingRole.LoaderCommand);
        Assert.Single(result.Document.Nodes.Single(x => x.Id == "concrete_bucket").Bindings, x => x.Role == SignalBindingRole.TargetCommand);
        Assert.DoesNotContain(result.Document.Nodes.Single(x => x.Id == "bsu_1").Bindings, x => x.Role == SignalBindingRole.LoaderOffFeedback);
        Assert.DoesNotContain(result.Document.Nodes.Single(x => x.Id == "concrete_bucket").Bindings, x => x.Role == SignalBindingRole.TargetOffFeedback);
    }

    [Fact]
    public void Migrator_v6_to_v8_removes_all_legacy_off_feedback_and_forces_toggle_buttons()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 6;
        document.Cards.Single().StartButtonKind = RouteCommandButtonKind.Momentary;
        document.Cards.Single().StopButtonKind = RouteCommandButtonKind.Momentary;
        document.TopBar.Emergency.ButtonKind = RouteCommandButtonKind.Momentary;
        document.TopBar.Automatic.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.AutomaticModeOffFeedback,
            SignalId = "system.mode.automatic.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });
        document.TopBar.Manual.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.ManualModeOffFeedback,
            SignalId = "system.mode.manual.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });
        document.Nodes.Single(x => x.Id == "bsu_1").Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.LoaderOffFeedback,
            SignalId = "route.node.bsu_1.loader.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.DoesNotContain(result.Document.TopBar.Automatic.Bindings, x => x.Role == SignalBindingRole.AutomaticModeOffFeedback);
        Assert.DoesNotContain(result.Document.TopBar.Manual.Bindings, x => x.Role == SignalBindingRole.ManualModeOffFeedback);
        Assert.DoesNotContain(result.Document.Nodes.Single(x => x.Id == "bsu_1").Bindings, x => x.Role == SignalBindingRole.LoaderOffFeedback);
        Assert.Equal(RouteCommandButtonKind.Toggle, result.Document.TopBar.Emergency.ButtonKind);
        Assert.Equal(RouteCommandButtonKind.Momentary, result.Document.Cards.Single().StartButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, result.Document.Cards.Single().StopButtonKind);
        Assert.False(result.Document.TopBar.Emergency.OffFeedbackEnabled);
        Assert.False(result.Document.Cards.Single().StartOffFeedbackEnabled);
        Assert.False(result.Document.Cards.Single().StopOffFeedbackEnabled);
        Assert.DoesNotContain(result.Document.TopBar.Emergency.Bindings, x => x.Role == SignalBindingRole.EmergencyOffFeedback);
        Assert.DoesNotContain(result.Document.Cards.Single().Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        Assert.DoesNotContain(result.Document.Cards.Single().Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);
    }

    [Fact]
    public void Migrator_preserves_existing_card_start_stop_off_feedback()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 6;
        var card = document.Cards.Single();
        card.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.StartOffFeedback,
            SignalId = "equip.bucket.start.off",
            Direction = SignalBindingDirection.Write,
            ValueType = SignalValueType.UInt16
        });
        card.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.StopOffFeedback,
            SignalId = "equip.bucket.stop.off",
            Direction = SignalBindingDirection.Write,
            ValueType = SignalValueType.UInt16
        });

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        var migratedCard = result.Document.Cards.Single();
        var startOff = Assert.Single(migratedCard.Bindings, x => x.Role == SignalBindingRole.StartOffFeedback);
        var stopOff = Assert.Single(migratedCard.Bindings, x => x.Role == SignalBindingRole.StopOffFeedback);
        Assert.True(migratedCard.StartOffFeedbackEnabled);
        Assert.True(migratedCard.StopOffFeedbackEnabled);
        Assert.Equal(SignalBindingDirection.Read, startOff.Direction);
        Assert.Equal(SignalBindingDirection.Read, stopOff.Direction);
        Assert.Equal(SignalValueType.Bool, startOff.ValueType);
        Assert.Equal(SignalValueType.Bool, stopOff.ValueType);
    }

    [Fact]
    public void Migrator_v8_to_v9_removes_legacy_state_bindings_and_updates_default_card_status()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 8;
        document.Nodes.Single(x => x.Id == "bsu_1").Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.State,
            SignalId = "bsu_1.state",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.String
        });
        document.Segments.Single(x => x.Id == "bsu2_to_bucket").Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.State,
            SignalId = "bsu2_to_bucket.state",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.String
        });
        var card = document.Cards.Single();
        card.StatusText = "Ожидание";
        card.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.State,
            SignalId = "equip.bucket.state",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.UInt16
        });

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.DoesNotContain(result.Document.Nodes.SelectMany(x => x.Bindings), x => x.Role == SignalBindingRole.State);
        Assert.DoesNotContain(result.Document.Segments.SelectMany(x => x.Bindings), x => x.Role == SignalBindingRole.State);
        Assert.DoesNotContain(result.Document.Cards.SelectMany(x => x.Bindings), x => x.Role == SignalBindingRole.State);
        Assert.Equal("Выключено", result.Document.Cards.Single().StatusText);
    }

    [Fact]
    public void Migrator_v9_to_current_adds_fragment_bindings()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 9;
        foreach (var segment in document.Segments)
            segment.ActiveFragments.Clear();

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal(
            Enumerable.Range(1, 3).Select(index => $"route.bsu2_to_bucket.fragment_{index}.active"),
            result.Document.Segments.Single(x => x.Id == "bsu2_to_bucket").ActiveFragments.Select(fragment => fragment.Binding.SignalId));
    }

    [Fact]
    public void Validator_rejects_legacy_off_feedback_roles()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Nodes.Single(x => x.Id == "bsu_1").Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.TargetOffFeedback,
            SignalId = "route.node.bsu_1.target.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });
        document.TopBar.Automatic.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.AutomaticModeOffFeedback,
            SignalId = "system.mode.automatic.off",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.Bool
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.TargetOffFeedback)));
        Assert.DoesNotContain(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.AutomaticModeOffFeedback)));
    }

    [Fact]
    public void Validator_rejects_legacy_state_role()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Cards.Single().Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.State,
            SignalId = "equip.bucket.state",
            Direction = SignalBindingDirection.Read,
            ValueType = SignalValueType.UInt16
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.State)));
    }

    [Fact]
    public void Validator_rejects_invalid_enabled_and_power_feedback_contracts()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        var powerBinding = card.Bindings.Single(x => x.Role == SignalBindingRole.PowerFeedback);
        powerBinding.Direction = SignalBindingDirection.ReadWrite;
        powerBinding.SignalId = card.Bindings.Single(x => x.Role == SignalBindingRole.StartCommand).SignalId;
        document.Nodes.Single(node => node.Id == "wash").Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.Enabled,
            SignalId = "route.node.enabled",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.UInt16,
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Message.Contains("PowerFeedback должен иметь направление Read"));
        Assert.Contains(result.Errors, x => x.Message.Contains("PowerFeedback должен иметь отдельный SignalId"));
        Assert.Contains(result.Errors, x => x.Message.Contains("Enabled должен иметь направление Read"));
        Assert.Contains(result.Errors, x => x.Message.Contains("Enabled должен иметь тип Bool"));
    }

    [Fact]
    public void Validator_checks_present_legacy_top_bar_bindings_without_requiring_them()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.TopBar.Automatic.Bindings.Clear();
        document.TopBar.Manual.Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.Enabled,
            SignalId = "system.mode.manual.enabled",
            Direction = SignalBindingDirection.Write,
            ValueType = SignalValueType.UInt16,
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.DoesNotContain(result.Errors, error => error.ObjectId == "automatic");
        Assert.Contains(result.Errors, error => error.ObjectId == "manual" && error.Message.Contains("направление Read"));
        Assert.Contains(result.Errors, error => error.ObjectId == "manual" && error.Message.Contains("тип Bool"));
    }

    [Fact]
    public void Validator_requires_reset_command_binding()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.TopBar.Reset.Bindings.Clear();

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.ResetCommand)));
    }

    [Fact]
    public async Task Top_bar_commands_use_configured_signal_ids()
    {
        var settings = RouteMapSeed.Create().TopBar! with
        {
            Automatic = RouteMapSeed.Create().TopBar!.Automatic with
            {
                Binding = new SignalBinding(SignalBindingRole.AutomaticModeCommand, "custom.auto", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
            },
            Manual = RouteMapSeed.Create().TopBar!.Manual with
            {
                Binding = new SignalBinding(SignalBindingRole.ManualModeCommand, "custom.manual", SignalBindingDirection.ReadWrite, SignalValueType.Bool),
            },
        };
        var dispatcher = new CapturingDispatcher();
        var viewModel = new TopBarViewModel(commandDispatcher: dispatcher, settings: settings);

        await viewModel.SwitchToAutomaticCommand.Execute().FirstAsync();

        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task Top_bar_emergency_ignores_legacy_momentary_and_dispatches_toggle_values()
    {
        var seed = RouteMapSeed.Create().TopBar!;
        var settings = seed with
        {
            Emergency = seed.Emergency with { ButtonKind = RouteCommandButtonKind.Momentary }
        };
        var dispatcher = new CapturingDispatcher();
        var viewModel = new TopBarViewModel(commandDispatcher: dispatcher, settings: settings);

        await viewModel.EmergencyCommand.Execute().FirstAsync();
        await viewModel.EmergencyCommand.Execute().FirstAsync();

        Assert.False(viewModel.HasEmergency);
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("system.emergency", true), (request.SignalId, request.Value)),
            request => Assert.Equal(("system.emergency", false), (request.SignalId, request.Value)));
    }

    [Fact]
    public async Task Top_bar_toggle_emergency_dispatches_false_when_off_feedback_is_disabled()
    {
        var seed = RouteMapSeed.Create().TopBar!;
        var settings = seed with
        {
            Emergency = seed.Emergency with
            {
                OffFeedbackEnabled = false,
                OffFeedbackBinding = null
            }
        };
        var dispatcher = new CapturingDispatcher();
        var viewModel = new TopBarViewModel(commandDispatcher: dispatcher, settings: settings);

        await viewModel.EmergencyCommand.Execute().FirstAsync();
        await viewModel.EmergencyCommand.Execute().FirstAsync();

        Assert.False(viewModel.HasEmergency);
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("system.emergency", true), (request.SignalId, request.Value)),
            request => Assert.Equal(("system.emergency", false), (request.SignalId, request.Value)));
    }

    [Fact]
    public async Task Top_bar_emergency_failure_restores_readback_reports_error_and_does_not_fault_command()
    {
        var errors = new List<string>();
        var dispatcher = new RecordingFailingDispatcher(failOnRequest: 1);
        var viewModel = new TopBarViewModel(
            commandDispatcher: dispatcher,
            settings: RouteMapSeed.Create().TopBar,
            commandErrorHandler: errors.Add);
        viewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            isResetActive: false,
            hasEmergency: false,
            isConnectionAvailable: true);
        Exception? thrownException = null;
        using var exceptionSubscription = viewModel.EmergencyCommand.ThrownExceptions.Subscribe(exception => thrownException = exception);

        await viewModel.EmergencyCommand.Execute().FirstAsync();

        Assert.Null(thrownException);
        Assert.False(viewModel.HasEmergency);
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("system.emergency", true), (request.SignalId, request.Value)));
        Assert.Single(errors);
        Assert.Contains("АВАРИЯ", errors[0]);
        Assert.Contains("Write failed", errors[0]);
    }

    [Fact]
    public async Task Top_bar_automatic_command_is_inert_even_when_enabled()
    {
        var errors = new List<string>();
        var dispatcher = new RecordingFailingDispatcher(failOnRequest: 2);
        var viewModel = new TopBarViewModel(
            commandDispatcher: dispatcher,
            settings: RouteMapSeed.Create().TopBar,
            commandErrorHandler: errors.Add);
        viewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            isResetActive: false,
            hasEmergency: false,
            isConnectionAvailable: true);

        await viewModel.SwitchToAutomaticCommand.Execute().FirstAsync();

        Assert.False(viewModel.IsAutomaticMode);
        Assert.True(viewModel.IsManualMode);
        Assert.Empty(dispatcher.Requests);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task Dashboard_node_role_command_error_rolls_back_state_and_sets_message()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var dispatcher = new FailingDispatcher();
        var journal = new RouteMapSessionJournal();
        using var notificationsPanel = new NotificationsPanelViewModel(
            journal,
            new NoOpDialogService(),
            new NoOpBitWriter());
        using var viewModel = new RouteMapDashboardViewModel(
            manager,
            new EmptySignalProvider(),
            new RouteMapRuntimeMapper(manager.CurrentDefinition),
            dispatcher,
            new RecordingSettingsDialogService(),
            notificationsPanel,
            journal,
            new StaticOptionsMonitor(new ModbusOptions()));

        await viewModel.ToggleNodeLoaderCommand.Execute("bsu_1").FirstAsync();

        Assert.True(viewModel.NodeRoleStates["bsu_1"].IsLoader);
        Assert.True(viewModel.HasCommandError);
        Assert.Contains("Write failed", viewModel.CommandErrorMessage);
    }

    [Theory]
    [InlineData(null, ModbusWriteMode.Latched, false)]
    [InlineData(ModbusDataAccess.Write, ModbusWriteMode.Latched, false)]
    [InlineData(ModbusDataAccess.ReadWrite, ModbusWriteMode.Pulse, false)]
    [InlineData(ModbusDataAccess.ReadWrite, ModbusWriteMode.Pulse, true)]
    public async Task Dashboard_rejects_missing_or_incompatible_wash_mapping_before_any_write(
        ModbusDataAccess? washAccess, ModbusWriteMode writeMode, bool washWasTarget)
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        if (washWasTarget)
        {
            var draft = manager.CreateDraft();
            draft.Nodes.Single(node => node.Id == "wash").IsTarget = true;
            draft.Nodes.Single(node => node.Id == "concrete_bucket").IsTarget = false;
            Assert.True(manager.Apply(draft).IsSuccess);
        }
        var provider = new EmptySignalProvider();
        var dispatcher = new CapturingDispatcher();
        using var runtime = new RouteMapSignalRuntime(provider, dispatcher, provider, dispatcher, RouteMapSignalSource.Modbus);
        var options = new ModbusOptions
        {
            DataMap =
            [
                new() { Name = "route.node.concrete_bucket.target", Type = ModbusValueType.Bool, Access = ModbusDataAccess.ReadWrite },
            ],
        };
        if (washAccess is { } access)
            options.DataMap.Add(new() { Name = "route.node.wash.target", Type = ModbusValueType.Bool, Access = access, WriteMode = writeMode });
        var journal = new RouteMapSessionJournal();
        using var notifications = new NotificationsPanelViewModel(journal, new NoOpDialogService(), new NoOpBitWriter());
        using var viewModel = new RouteMapDashboardViewModel(manager, runtime, new RouteMapRuntimeMapper(manager), runtime,
            new RecordingSettingsDialogService(), notifications, journal, new StaticOptionsMonitor(options));

        await viewModel.ToggleNodeTargetCommand.Execute(washWasTarget ? "concrete_bucket" : "wash").FirstAsync();

        Assert.Empty(dispatcher.Requests);
        Assert.Equal(washWasTarget ? "wash" : "concrete_bucket",
            Assert.Single(viewModel.NodeRoleStates.Values, state => state.IsTarget).ObjectId);
        Assert.True(viewModel.NodeRoleStates["bsu_1"].IsLoader);
        Assert.Contains("route.node.wash.target", viewModel.CommandErrorMessage);
        Assert.Contains("Modbus.DataMap", viewModel.CommandErrorMessage);
    }

    [Fact]
    public async Task Dashboard_ignores_role_commands_for_terminal_anchors()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var draft = manager.CreateDraft();
        var (anchor, _) = ConfigureTerminal(draft, RouteTerminalSide.From);
        Assert.True(manager.Apply(draft).IsSuccess);

        var dispatcher = new CapturingDispatcher();
        var journal = new RouteMapSessionJournal();
        using var viewModel = new RouteMapDashboardViewModel(
            manager,
            new EmptySignalProvider(),
            new RouteMapRuntimeMapper(manager),
            dispatcher,
            new RecordingSettingsDialogService(),
            new NotificationsPanelViewModel(journal, new NoOpDialogService(), new NoOpBitWriter()),
            journal,
            new StaticOptionsMonitor(new ModbusOptions()));
        await viewModel.ToggleNodeLoaderCommand.Execute(anchor.Id).FirstAsync();
        await viewModel.ToggleNodeTargetCommand.Execute(anchor.Id).FirstAsync();

        Assert.DoesNotContain(anchor.Id, viewModel.NodeRoleStates.Keys);
        Assert.True(viewModel.NodeRoleStates["bsu_1"].IsLoader);
        Assert.True(viewModel.NodeRoleStates["concrete_bucket"].IsTarget);
        Assert.Empty(dispatcher.Requests);
    }

    [Fact]
    public async Task Dashboard_displays_top_bar_command_error()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        var dispatcher = new FailingDispatcher();
        var journal = new RouteMapSessionJournal();
        using var notificationsPanel = new NotificationsPanelViewModel(
            journal,
            new NoOpDialogService(),
            new NoOpBitWriter());
        using var viewModel = new RouteMapDashboardViewModel(
            manager,
            new EmptySignalProvider(),
            new RouteMapRuntimeMapper(manager.CurrentDefinition),
            dispatcher,
            new RecordingSettingsDialogService(),
            notificationsPanel,
            journal,
            new StaticOptionsMonitor(new ModbusOptions()));

        await viewModel.TopBar.EmergencyCommand.Execute().FirstAsync();

        Assert.True(viewModel.HasCommandError);
        Assert.Contains("АВАРИЯ", viewModel.CommandErrorMessage);
        Assert.Contains("Write failed", viewModel.CommandErrorMessage);
    }

    [Fact]
    public async Task Node_role_command_writer_dispatches_false_resets_before_true_selection()
    {
        var definition = RouteMapSeed.Create();
        var previous = definition.Nodes.ToDictionary(x => x.Id, x => new RouteNodeRoleState(x.Id, x.IsLoader, x.IsTarget));
        var current = RouteNodeRoleStateTransitions.ToggleTarget(previous, "bsu_1");
        var dispatcher = new CapturingDispatcher();

        await RouteNodeRoleCommandWriter.DispatchTransitionAsync(definition, previous, current, dispatcher);

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("route.node.bsu_1.loader", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("route.node.concrete_bucket.target", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("route.node.bsu_1.target", true), (request.SignalId, request.Value)));
    }

    private sealed class StatusTestFilePicker(string importPath, string exportPath) : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(importPath);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(exportPath);
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private static Color BrushColor(IBrush brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    private sealed class RecordingSettingsDialogService : IRouteMapSettingsDialogService
    {
        public int CallCount { get; private set; }
        public Task ShowAsync(CancellationToken cancellationToken = default) { CallCount++; return Task.CompletedTask; }
    }

    private sealed class CapturingDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingDispatcher : IEquipmentCommandDispatcher
    {
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromException(new InvalidOperationException("Write failed"));
    }

    private sealed class RecordingFailingDispatcher(int failOnRequest) : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Requests.Count == failOnRequest
                ? Task.FromException(new InvalidOperationException("Write failed"))
                : Task.CompletedTask;
        }
    }

    private sealed class RecordingHelpDialogService : IHelpDialogService
    {
        public int CallCount { get; private set; }
        public Task ShowAsync(CancellationToken cancellationToken = default) { CallCount++; return Task.CompletedTask; }
    }

    [Fact]
    public async Task Top_bar_reset_dispatches_single_pulse_true_value()
    {
        var dispatcher = new CapturingDispatcher();
        var viewModel = new TopBarViewModel(commandDispatcher: dispatcher, settings: RouteMapSeed.Create().TopBar);

        await viewModel.ResetCommand.Execute().FirstAsync();
        await viewModel.ResetCommand.Execute().FirstAsync();

        Assert.False(viewModel.IsResetActive);
        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("system.reset", true), (request.SignalId, request.Value)),
            request => Assert.Equal(("system.reset", true), (request.SignalId, request.Value)));
    }

    private sealed class NoOpBitWriter : IModbusBitWriter
    {
        public Task<ModbusOperationResult> PulseAsync(
            ModbusBitAddressOptions address,
            int pulseDurationMs,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ModbusOperationResult.Success());
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public Task<bool> ConfirmAsync(string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<string?> RequestSecretAsync(string message, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task ShowErrorAsync(string title, string message, string? details = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task<bool> ShowAlarmNotificationAsync(ModbusAlarmKind kind, string message, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ModbusOptions?> EditModbusSettingsAsync(string title, string sectionName, ModbusOptions options, CancellationToken ct = default) => Task.FromResult<ModbusOptions?>(null);
        public Task<OpcUaConfiguredTag?> EditOpcUaTagAsync(string title, OpcUaConfiguredTag? tag, OpcUaImportTarget target, CancellationToken ct = default) => Task.FromResult<OpcUaConfiguredTag?>(null);
        public Task<OpcUaTagImportResult?> ImportOpcUaTagsAsync(OpcUaBrowseRequest request, CancellationToken ct = default) => Task.FromResult<OpcUaTagImportResult?>(null);
    }

    private sealed class StaticOptionsMonitor(ModbusOptions options) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = options;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class EmptySignalProvider : ISignalValueProvider
    {
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() =>
            Observable.Empty<IReadOnlyDictionary<string, SignalValue>>();
    }

    private sealed class RejectingSynchronizationContext : SynchronizationContext
    {
        public int PostCount { get; private set; }

        public override void Post(SendOrPostCallback d, object? state)
        {
            PostCount++;
            throw new InvalidOperationException("Startup must not post async continuations to the UI context.");
        }
    }

    private sealed class TempConfigurationScope : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "route-map-tests", Guid.NewGuid().ToString("N"));

        public TempConfigurationScope()
        {
            Directory.CreateDirectory(_directory);
            Path = System.IO.Path.Combine(_directory, "route-map.json");
            Storage = new RouteMapConfigurationStorage(Path);
            Mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
        }

        public string Path { get; }
        public RouteMapConfigurationStorage Storage { get; }
        public RouteMapConfigurationMapper Mapper { get; }
        public RouteMapConfigurationManager CreateManager() => new(
            Storage,
            Mapper,
            new RouteMapConfigurationValidator(),
            new RouteMapConfigurationMigrator());
        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }
    }
}
