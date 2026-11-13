using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Configurator.Application.Services;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using DialogHostAvalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class EquipmentCardParametersDialogTests
{
    private const string UserSignalId = "equip.bucket.speed";
    private const string AdminSignalId = "equip.bucket.parameter2";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    // UI-07, UI-08
    [AvaloniaTheory]
    [InlineData(ApplicationOptions.AdminWorkMode, true)]
    [InlineData(ApplicationOptions.UserWorkMode, false)]
    public async Task Work_mode_controls_rows_and_technical_captions(string workMode, bool isAdminMode)
    {
        using var fixture = new DialogFixture(workMode);
        var definition = fixture.Manager.CurrentDefinition;
        var document = fixture.Manager.CurrentDocument;
        var originalJson = File.ReadAllText(fixture.Storage.ActiveFilePath);
        var dialog = fixture.Open();

        var texts = VisibleTexts(dialog);
        Assert.Contains("Кюбель Л.К.", texts);
        Assert.Contains("Настройки оборудования", texts);
        Assert.Contains("Скорость", texts);
        Assert.Equal(isAdminMode, texts.Contains("Фильтер"));
        Assert.Equal(isAdminMode, texts.Contains($"{UserSignalId} • UInt16"));
        Assert.Equal(isAdminMode, texts.Contains($"{AdminSignalId} • UInt16"));
        Assert.Equal(isAdminMode ? 2 : 1, Assert.Single(dialog.GetVisualDescendants().OfType<ItemsControl>()).Items.Count);
        Assert.True(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);

        await fixture.CloseAsync(dialog);
        Assert.Same(definition, fixture.Manager.CurrentDefinition);
        Assert.Same(document, fixture.Manager.CurrentDocument);
        Assert.Equal(originalJson, File.ReadAllText(fixture.Storage.ActiveFilePath));
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    // UI-09
    [AvaloniaFact]
    public async Task All_admin_parameters_leave_an_empty_dialog_that_opens_and_closes_offline()
    {
        using var fixture = new DialogFixture(ApplicationOptions.UserWorkMode, allAdmin: true, offline: true);
        var originalJson = File.ReadAllText(fixture.Storage.ActiveFilePath);
        var dialog = fixture.Open();

        var texts = VisibleTexts(dialog);
        Assert.Contains("Кюбель Л.К.", texts);
        Assert.Contains("Настройки оборудования", texts);
        Assert.Contains("Для текущего режима нет доступных настроек оборудования.", texts);
        Assert.Empty(Assert.Single(dialog.GetVisualDescendants().OfType<ItemsControl>()).Items);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        Assert.True(FindButton(dialog, "Закрыть").IsEffectivelyEnabled);

        await fixture.CloseAsync(dialog);
        Assert.Equal(originalJson, File.ReadAllText(fixture.Storage.ActiveFilePath));
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    // UI-10
    [AvaloniaFact]
    public async Task Saving_visible_bucket_parameter_preserves_hidden_setpoint_and_full_configuration()
    {
        using var fixture = new DialogFixture(ApplicationOptions.UserWorkMode);
        var originalParameters = fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters;
        var originalInventory = RouteMapSignalInventory.Build(fixture.Manager.CurrentDefinition);
        Assert.Contains(originalInventory, item => item.SignalId == AdminSignalId);
        var expectedJson = JsonNode.Parse(File.ReadAllText(fixture.Storage.ActiveFilePath))!;
        var userParameterJson = expectedJson["cards"]!.AsArray().Single()!["parameters"]!.AsArray()
            .Single(parameter => (string?)parameter!["signalId"] == UserSignalId)!;
        userParameterJson["savedValue"] = "42";
        var dialog = fixture.Open();
        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), box => box.IsEffectivelyVisible);
        var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
        Assert.Equal("10", input.Text);
        input.SetCurrentValue(TextBox.TextProperty, "42");
        Assert.Equal("42", Assert.Single(viewModel.Parameters).ValueText);

        var saveCompleted = viewModel.SaveCommand.FirstAsync().ToTask();
        fixture.Click(FindButton(dialog, "Сохранить"));
        await saveCompleted.WaitAsync(TestTimeout);
        Assert.Null(viewModel.ErrorMessage);
        var request = Assert.Single(fixture.Dispatcher.Requests);
        Assert.Equal((UserSignalId, (ushort)42, SignalValueType.UInt16), (request.SignalId, request.Value, request.ValueType));
        Assert.Equal(new[] { originalParameters[0] with { SavedValue = "42" }, originalParameters[1] },
            fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters);
        var hidden = Assert.Single(fixture.Manager.CurrentDocument.Cards.Single().Parameters,
            parameter => parameter.SignalId == AdminSignalId);
        Assert.Equal((EquipmentParameterVisibility.Admin, "5000", true, "Предыдущая ошибка"),
            (hidden.Visibility, hidden.SavedValue, hidden.PendingAutoDispatch, hidden.LastDispatchError));
        Assert.Equal(originalParameters[1], Assert.Single(fixture.ValueStore.GetPending()).Parameter);
        Assert.Equal(originalInventory, RouteMapSignalInventory.Build(fixture.Manager.CurrentDefinition));
        Assert.True(JsonNode.DeepEquals(expectedJson, JsonNode.Parse(File.ReadAllText(fixture.Storage.ActiveFilePath))));

        await fixture.CloseAsync(dialog);
        Assert.Single(fixture.Dispatcher.Requests);
    }

    private static string?[] VisibleTexts(Control dialog) => dialog.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible)
        .Select(text => text.Text)
        .ToArray();

    private static Button FindButton(Control dialog, string content) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, content));

    private sealed class DialogFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"equipment-dialog-{Guid.NewGuid():N}");
        private readonly DialogHostStyles _hostStyles = new();
        private readonly ServiceProvider _services;
        private readonly DialogHost _host;
        private readonly Window _window;
        private readonly EquipmentCardView _cardView;

        public DialogFixture(string workMode, bool allAdmin = false, bool offline = false)
        {
            Storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
            var document = mapper.CreateSeedDocument();
            var bucket = Assert.Single(document.Cards, card => card.Id == "equip.bucket");
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Скорость",
                SignalId = UserSignalId,
                ValueType = SignalValueType.UInt16,
                Visibility = allAdmin ? EquipmentParameterVisibility.Admin : EquipmentParameterVisibility.User,
                SavedValue = "10",
            });
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Фильтер",
                SignalId = AdminSignalId,
                ValueType = SignalValueType.UInt16,
                Visibility = EquipmentParameterVisibility.Admin,
                SavedValue = "5000",
                PendingAutoDispatch = true,
                LastDispatchError = "Предыдущая ошибка",
            });
            Storage.SaveActive(document);
            Manager = new RouteMapConfigurationManager(
                Storage, mapper, new RouteMapConfigurationValidator(), new RouteMapConfigurationMigrator());
            Assert.Null(Manager.LastLoadError);
            ValueStore = new RouteMapEquipmentParameterValueStore(Manager);
            _services = new ServiceCollection()
                .AddSingleton<IEquipmentCommandDispatcher>(Dispatcher)
                .AddSingleton<IEquipmentParameterWriteValidator>(NoopEquipmentParameterWriteValidator.Instance)
                .AddSingleton<IEquipmentParameterValueStore>(ValueStore)
                .AddTransient<EquipmentCardParametersDialogView>()
                .BuildServiceProvider();
            var dialogService = new EquipmentCardParametersDialogService(
                _services, new DialogCoordinator(), Options.Create(new ApplicationOptions { WorkMode = workMode }));
            var card = Manager.CurrentDefinition.MapEquipment.Single();
            var cardViewModel = new EquipmentCardViewModel(card, Dispatcher, parametersDialogService: dialogService);
            cardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
                card.Id, offline ? RouteObjectState.Offline : RouteObjectState.Idle, card.StatusText, ValueText: null,
                IsVisible: true, CanStart: false, CanStop: false, IsEnabled: !offline), isConnectionAvailable: !offline);

            Avalonia.Application.Current!.Styles.Add(_hostStyles);
            _cardView = new EquipmentCardView { DataContext = cardViewModel };
            _host = new DialogHost
            {
                Identifier = DialogHostIds.Root,
                IsMultipleDialogsEnabled = true,
                Content = _cardView,
            };
            _window = new Window { Width = 700, Height = 480, Content = _host };
            _window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public RouteMapConfigurationStorage Storage { get; }
        public RouteMapConfigurationManager Manager { get; }
        public RouteMapEquipmentParameterValueStore ValueStore { get; }
        public RecordingDispatcher Dispatcher { get; } = new();

        public EquipmentCardParametersDialogView Open()
        {
            var button = _cardView.FindControl<Button>("ParametersButton")!;
            Assert.True(button.IsEffectivelyEnabled);
            Click(button);
            Assert.True(_host.IsOpen);
            return Assert.Single(_host.GetVisualDescendants().OfType<EquipmentCardParametersDialogView>());
        }

        public async Task CloseAsync(EquipmentCardParametersDialogView dialog)
        {
            var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
            var closed = viewModel.Result.FirstAsync().ToTask();
            Click(FindButton(dialog, "Закрыть"));
            Assert.False(await closed.WaitAsync(TestTimeout));
            Assert.False(_host.IsOpen);
        }

        public void Click(Button button)
        {
            var center = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
            var point = button.TranslatePoint(center, _window)!.Value;
            _window.MouseMove(point, RawInputModifiers.None);
            _window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            _window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (_host.IsOpen)
                DialogHost.Close(DialogHostIds.Root, false);
            _window.Close();
            Avalonia.Application.Current!.Styles.Remove(_hostStyles);
            _services.Dispose();
            ValueStore.Dispose();
            Manager.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }
}
