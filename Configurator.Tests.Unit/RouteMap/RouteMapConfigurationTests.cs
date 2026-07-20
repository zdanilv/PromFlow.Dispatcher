using System.Reactive.Linq;
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
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.Unit.RouteMap;

public sealed class RouteMapConfigurationTests
{
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
        Assert.Equal(RouteCommandButtonKind.Toggle, loaded.Cards.Single().StartButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, loaded.Cards.Single().StopButtonKind);
        Assert.Equal(RouteCommandButtonKind.Toggle, loaded.TopBar.Emergency.ButtonKind);
        Assert.Single(loaded.TopBar.Reset.Bindings, binding => binding.Role == SignalBindingRole.ResetCommand);
        Assert.False(loaded.Cards.Single().StartOffFeedbackEnabled);
        Assert.False(loaded.TopBar.Emergency.OffFeedbackEnabled);
        Assert.DoesNotContain(loaded.Cards.Single().Bindings, binding => binding.Role == SignalBindingRole.StartOffFeedback);
        Assert.DoesNotContain(loaded.TopBar.Emergency.Bindings, binding => binding.Role == SignalBindingRole.EmergencyOffFeedback);
        var json = await File.ReadAllTextAsync(scope.Path);
        Assert.Contains("\"roundedElbow90\"", json);
        Assert.Contains("\"startButtonKind\": \"toggle\"", json);
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
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 1;
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
        Assert.Equal((240d, 500d), (lower.X, lower.Y));
        Assert.Equal((10d, 0d), (lower.LabelOffsetX, lower.LabelOffsetY));
        Assert.Equal(RouteNodeLabelPlacement.Right, lower.LabelPlacement);
        Assert.Equal(150, manager.CurrentDocument.Segments.Single(x => x.Id == "bsu2_to_bucket").ArcRadius);
        var persisted = await scope.Storage.LoadActiveAsync();
        Assert.NotNull(persisted);
        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, persisted.SchemaVersion);
        Assert.Equal(RouteCommandButtonKind.Toggle, persisted.TopBar.Emergency.ButtonKind);
        Assert.All(persisted.Cards, card =>
        {
            Assert.Equal(RouteCommandButtonKind.Toggle, card.StartButtonKind);
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
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 1;
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
    public void Migrator_v11_to_current_adds_selector_bindings_reset_and_updates_only_legacy_disabled_color()
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
            var uncheckedBinding = Assert.Single(card.Bindings, x => x.Role == SignalBindingRole.UncheckedCommand);
            var checkedBinding = Assert.Single(card.Bindings, x => x.Role == SignalBindingRole.CheckedCommand);
            Assert.Equal($"{card.Id}.selector.off", uncheckedBinding.SignalId);
            Assert.Equal($"{card.Id}.selector.on", checkedBinding.SignalId);
            Assert.Equal(SignalBindingDirection.ReadWrite, uncheckedBinding.Direction);
            Assert.Equal(SignalValueType.Bool, checkedBinding.ValueType);
        });

        var custom = scope.Mapper.CreateSeedDocument();
        custom.SchemaVersion = 11;
        custom.Map.Palette.Disabled = "#123456";
        new RouteMapConfigurationMigrator().Migrate(custom);
        Assert.Equal("#123456", custom.Map.Palette.Disabled);
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

        using var manager = scope.CreateManager();

        Assert.Null(manager.LastLoadError);
        Assert.Equal(15, manager.CurrentDocument.SchemaVersion);
        Assert.Equal("custom.auto", manager.CurrentDocument.TopBar.Automatic.Bindings.Single(x => x.Role == SignalBindingRole.AutomaticModeCommand).SignalId);

        var persisted = await File.ReadAllTextAsync(scope.Path);
        Assert.False(persisted.Contains("normalBackground", StringComparison.Ordinal));
        Assert.False(persisted.Contains("checkedForeground", StringComparison.Ordinal));
        Assert.False(persisted.Contains("startColor", StringComparison.Ordinal));
        Assert.False(persisted.Contains("stopPressedForegroundColor", StringComparison.Ordinal));
    }

    [Fact]
    public void Migrator_updates_v2_visual_defaults_and_preserves_custom_active_binding()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 2;
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
        Assert.All(result.Document.Segments, item =>
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
        Assert.Single(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.UncheckedCommand);
        Assert.Single(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.CheckedCommand);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.NodeBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.SegmentBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.CardBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.AutomaticModeBindingRoles);
        Assert.Contains(SignalBindingRole.ResetCommand, viewModel.ResetBindingRoles);
        Assert.Contains(SignalBindingRole.Enabled, viewModel.ResetBindingRoles);
    }

    [Fact]
    public void Mapper_preserves_top_bar_enabled_bindings()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
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
    }

    [Fact]
    public void Settings_duplicate_card_rewrites_selector_signal_ids()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());

        viewModel.DuplicateCardCommand.Execute().Subscribe();

        var card = viewModel.SelectedCard!;
        Assert.Equal($"{card.Id}.selector.off", card.Bindings.Single(x => x.Role == SignalBindingRole.UncheckedCommand).SignalId);
        Assert.Equal($"{card.Id}.selector.on", card.Bindings.Single(x => x.Role == SignalBindingRole.CheckedCommand).SignalId);
    }

    [Fact]
    public void Settings_adds_and_removes_optional_top_bar_enabled_binding()
    {
        using var scope = new TempConfigurationScope();
        using var manager = scope.CreateManager();
        using var viewModel = new RouteMapSettingsViewModel(manager, scope.Storage, new NullFilePicker());

        viewModel.AddBindingCommand.Execute("automatic").Subscribe();
        var enabled = Assert.Single(viewModel.Draft.TopBar.Automatic.Bindings, x => x.Role == SignalBindingRole.Enabled);

        Assert.Equal("system.mode.automatic.enabled", enabled.SignalId);
        Assert.Equal(SignalBindingDirection.Read, enabled.Direction);
        Assert.Equal(SignalValueType.Bool, enabled.ValueType);

        viewModel.RemoveBindingCommand.Execute(enabled).Subscribe();
        Assert.DoesNotContain(viewModel.Draft.TopBar.Automatic.Bindings, x => x.Role == SignalBindingRole.Enabled);

        viewModel.AddBindingCommand.Execute("reset").Subscribe();
        var resetEnabled = Assert.Single(viewModel.Draft.TopBar.Reset.Bindings, x => x.Role == SignalBindingRole.Enabled);
        Assert.Equal("system.reset.enabled", resetEnabled.SignalId);

        viewModel.RemoveBindingCommand.Execute(resetEnabled).Subscribe();
        Assert.DoesNotContain(viewModel.Draft.TopBar.Reset.Bindings, x => x.Role == SignalBindingRole.Enabled);
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
        Assert.Contains(roundTrip.Cards.Single().Parameters, x => x.ValueType == SignalValueType.Date);
        Assert.Contains(roundTrip.Cards.Single().Parameters, x => x.ValueType == SignalValueType.Dword);
        Assert.Contains(roundTrip.Cards.Single().Parameters, x => x.ValueType == SignalValueType.Word);
    }

    [Fact]
    public void Validator_rejects_missing_required_node_card_and_top_bar_bindings()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Nodes[0].Bindings.Remove(document.Nodes[0].Bindings.Single(x => x.Role == SignalBindingRole.ActiveRoute));
        var card = document.Cards.Single();
        card.Bindings.Remove(card.Bindings.Single(x => x.Role == SignalBindingRole.StartCommand));
        document.TopBar.Automatic.Bindings.Clear();

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Scope == "node" && x.Message.Contains("ActiveRoute"));
        Assert.Contains(result.Errors, x => x.Scope == "card" && x.Message.Contains("StartCommand"));
        Assert.Contains(result.Errors, x => x.Scope == "topBar" && x.Message.Contains("AutomaticModeCommand"));
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
    public void Validator_rejects_momentary_command_buttons()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.Cards.Single().StartButtonKind = RouteCommandButtonKind.Momentary;
        document.Cards.Single().StopButtonKind = RouteCommandButtonKind.Toggle;
        document.TopBar.Emergency.ButtonKind = RouteCommandButtonKind.Momentary;

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Property == nameof(EquipmentCardConfiguration.StartButtonKind));
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
    public void Top_bar_button_colors_resolve_pressed_checked_normal_priority()
    {
        var viewModel = new TopBarViewModel(settingsDialogService: null, settings: RouteMapSeed.Create().TopBar);

        Assert.Equal(Color.Parse("#ECEFF1"), BrushColor(viewModel.AutomaticBackground));
        Assert.Equal(Color.Parse("#ECEFF1"), BrushColor(viewModel.ResetBackground));
        Assert.Equal(Color.Parse("#FF2626"), BrushColor(viewModel.EmergencyBackground));
        Assert.Equal(Color.Parse("#101820"), BrushColor(viewModel.EmergencyForeground));

        viewModel.ApplyRuntime(
            isAutomaticMode: true,
            isManualMode: false,
            isResetActive: true,
            hasEmergency: true,
            isConnectionAvailable: true);

        Assert.Equal(Color.Parse("#003CA3"), BrushColor(viewModel.AutomaticBackground));
        Assert.Equal(Color.Parse("#FFFFFF"), BrushColor(viewModel.AutomaticForeground));
        Assert.Equal(Color.Parse("#D1C300"), BrushColor(viewModel.ResetBackground));
        Assert.Equal(Color.Parse("#D10000"), BrushColor(viewModel.EmergencyBackground));

        viewModel.SetAutomaticPressed(true);
        Assert.Equal(Color.Parse("#8AB5FF"), BrushColor(viewModel.AutomaticBackground));
        Assert.Equal(Color.Parse("#FFFFFF"), BrushColor(viewModel.AutomaticForeground));

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
        Assert.Equal(RouteCommandButtonKind.Toggle, result.Document.Cards.Single().StartButtonKind);
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
        Assert.Contains(result.Errors, x => x.Message.Contains(nameof(SignalBindingRole.AutomaticModeOffFeedback)));
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
    public void Validator_rejects_invalid_enabled_and_selector_contracts()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        var card = document.Cards.Single();
        card.Bindings.Single(x => x.Role == SignalBindingRole.CheckedCommand).SignalId =
            card.Bindings.Single(x => x.Role == SignalBindingRole.UncheckedCommand).SignalId;
        document.Nodes.First().Bindings.Add(new SignalBindingConfiguration
        {
            Role = SignalBindingRole.Enabled,
            SignalId = "route.node.enabled",
            Direction = SignalBindingDirection.ReadWrite,
            ValueType = SignalValueType.UInt16,
        });

        var result = new RouteMapConfigurationValidator().Validate(document);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, x => x.Message.Contains("разные SignalId"));
        Assert.Contains(result.Errors, x => x.Message.Contains("Enabled должен иметь направление Read"));
        Assert.Contains(result.Errors, x => x.Message.Contains("Enabled должен иметь тип Bool"));
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

        Assert.Collection(dispatcher.Requests,
            request => Assert.Equal(("custom.manual", false), (request.SignalId, request.Value)),
            request => Assert.Equal(("custom.auto", true), (request.SignalId, request.Value)));
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
