using System.Reactive.Linq;
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
    public async Task Storage_round_trip_preserves_version_enums_and_styles()
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
        var json = await File.ReadAllTextAsync(scope.Path);
        Assert.Contains("\"roundedElbow90\"", json);
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
        Assert.Equal(4, manager.CurrentDocument.SchemaVersion);
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
        Assert.Equal(4, persisted.SchemaVersion);
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
        Assert.Equal(4, result.Document.SchemaVersion);
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

        Assert.Equal(4, result.Document.SchemaVersion);
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
        Assert.Single(viewModel.SelectedCard!.Bindings, x => x.Role == SignalBindingRole.StartCommand);
        Assert.Single(viewModel.SelectedCard.Bindings, x => x.Role == SignalBindingRole.StopCommand);
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
    public async Task Top_bar_settings_command_opens_dialog_service()
    {
        var service = new RecordingSettingsDialogService();
        var viewModel = new TopBarViewModel(service);

        await viewModel.OpenSettingsCommand.Execute().FirstAsync();

        Assert.Equal(1, service.CallCount);
    }

    [Fact]
    public void Migrator_adds_v4_node_and_top_bar_bindings_without_replacing_custom_signal_ids()
    {
        using var scope = new TempConfigurationScope();
        var document = scope.Mapper.CreateSeedDocument();
        document.SchemaVersion = 3;
        document.TopBar.Automatic.Bindings.Single().SignalId = "custom.auto";
        foreach (var node in document.Nodes)
        {
            node.Bindings = new System.Collections.ObjectModel.ObservableCollection<SignalBindingConfiguration>(
                node.Bindings.Where(x => x.Role is not SignalBindingRole.ActiveRoute
                    and not SignalBindingRole.TargetCommand
                    and not SignalBindingRole.LoaderCommand));
        }

        var result = new RouteMapConfigurationMigrator().Migrate(document);

        Assert.Equal(4, result.Document.SchemaVersion);
        Assert.Equal("custom.auto", result.Document.TopBar.Automatic.Bindings.Single().SignalId);
        Assert.All(result.Document.Nodes, node =>
        {
            var active = Assert.Single(node.Bindings, x => x.Role == SignalBindingRole.ActiveRoute);
            Assert.Equal($"route.node.{node.Id}.active", active.SignalId);
            Assert.Equal("#00A6A6", node.Style.ActiveOutlineColor);
            Assert.Equal(3, node.Style.ActiveOutlineThickness);
        });
        Assert.Single(result.Document.Nodes.Single(x => x.Id == "bsu_1").Bindings, x => x.Role == SignalBindingRole.LoaderCommand);
        Assert.Single(result.Document.Nodes.Single(x => x.Id == "concrete_bucket").Bindings, x => x.Role == SignalBindingRole.TargetCommand);
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
    public async Task Node_role_command_writer_dispatches_previous_reset_new_selection_and_cross_role_reset()
    {
        var definition = RouteMapSeed.Create();
        var previous = definition.Nodes.ToDictionary(x => x.Id, x => new RouteNodeRoleState(x.Id, x.IsLoader, x.IsTarget));
        var current = RouteNodeRoleStateTransitions.ToggleTarget(previous, "bsu_1");
        var dispatcher = new CapturingDispatcher();

        await RouteNodeRoleCommandWriter.DispatchTransitionAsync(definition, previous, current, dispatcher);

        Assert.Contains(dispatcher.Requests, x => x.SignalId == "route.node.concrete_bucket.target" && x.Value is false);
        Assert.Contains(dispatcher.Requests, x => x.SignalId == "route.node.bsu_1.target" && x.Value is true);
        Assert.Contains(dispatcher.Requests, x => x.SignalId == "route.node.bsu_1.loader" && x.Value is false);
        Assert.True(dispatcher.Requests.TakeWhile(x => x.Value is false).Count() >= 2);
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

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
