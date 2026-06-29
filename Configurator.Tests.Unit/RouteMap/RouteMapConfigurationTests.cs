using System.Reactive.Linq;
using Avalonia.Media;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
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
    public void Top_bar_button_colors_resolve_pressed_checked_normal_priority()
    {
        var viewModel = new TopBarViewModel(settingsDialogService: null, settings: RouteMapSeed.Create().TopBar);

        Assert.Equal(Color.Parse("#D95D4E"), BrushColor(viewModel.EmergencyBackground));

        viewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            hasEmergency: true,
            connectionStatusText: "Ожидание",
            isConnectionAvailable: true);

        Assert.Equal(Color.Parse("#9E2F25"), BrushColor(viewModel.EmergencyBackground));

        viewModel.SetEmergencyPressed(true);

        Assert.Equal(Color.Parse("#949595"), BrushColor(viewModel.EmergencyBackground));
        Assert.Equal(Color.Parse("#FFFFFF"), BrushColor(viewModel.EmergencyForeground));
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
    public void Migrator_v9_to_v10_adds_fragment_bindings_and_button_state_defaults()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 9;
        document.TopBar.Automatic.CheckedBackground = "#123456";
        document.TopBar.Emergency.NormalBackground = "#D87868";
        document.TopBar.Emergency.CheckedBackground = "#C83F30";
        var card = document.Cards.Single();
        card.Style.StartCheckedColor = "#0078D4";
        card.Style.StopCheckedColor = "#0078D4";
        foreach (var segment in document.Segments)
            segment.ActiveFragments.Clear();

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(RouteMapConfigurationDocument.CurrentSchemaVersion, result.Document.SchemaVersion);
        Assert.Equal("#123456", result.Document.TopBar.Automatic.CheckedBackground);
        Assert.Equal("#949595", result.Document.TopBar.Automatic.PressedBackground);
        Assert.Equal("#D95D4E", result.Document.TopBar.Emergency.NormalBackground);
        Assert.Equal("#949595", result.Document.TopBar.Emergency.PressedBackground);
        Assert.Equal("#9E2F25", result.Document.TopBar.Emergency.CheckedBackground);
        Assert.Equal("#3A9D5D", card.Style.StartCheckedColor);
        Assert.Equal("#9E2F25", card.Style.StopCheckedColor);
        Assert.Equal("#949595", card.Style.StartPressedColor);
        Assert.Equal("#949595", card.Style.StopPressedColor);
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
        using var viewModel = new RouteMapDashboardViewModel(
            manager,
            new EmptySignalProvider(),
            new RouteMapRuntimeMapper(manager.CurrentDefinition),
            dispatcher,
            new RecordingSettingsDialogService());

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
