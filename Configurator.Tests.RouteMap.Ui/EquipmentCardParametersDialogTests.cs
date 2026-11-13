using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
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
    private const string UserSignalId = "test.parameter.user.1";
    private const string AdminSignalId = "test.parameter.admin.1";
    private const string ReadSignalId = "test.parameter.user.2";
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
        Assert.Equal(isAdminMode, FindButton(dialog, "Добавить").IsEffectivelyVisible);
        Assert.DoesNotContain("Новый параметр", texts);
        Assert.Equal(isAdminMode ? 3 : 2, ParameterList(dialog).Items.Count);
        Assert.True(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);

        await fixture.CloseAsync(dialog);
        Assert.Same(definition, fixture.Manager.CurrentDefinition);
        Assert.Same(document, fixture.Manager.CurrentDocument);
        Assert.Equal(originalJson, File.ReadAllText(fixture.Storage.ActiveFilePath));
        Assert.Empty(fixture.Dispatcher.Requests);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(16)]
    public async Task Admin_can_open_and_cancel_the_form_with_keyboard_in_a_scrollable_dialog(int parameterCount)
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, parameterCount: parameterCount);
        var dialog = fixture.Open();
        var add = FindButton(dialog, "Добавить");
        var title = dialog.FindControl<TextBox>("NewParameterTitleBox")!;
        var form = dialog.FindControl<Border>("NewParameterForm")!;
        var scroll = dialog.FindControl<ScrollViewer>("ParametersScrollViewer")!;
        Assert.Equal(parameterCount == 0, VisibleTexts(dialog).Contains("Для текущего режима нет доступных настроек оборудования."));
        Assert.Equal(parameterCount > 0, FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        add.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        Assert.True(add.Focus());
        fixture.PressSpace();

        Assert.True(form.IsEffectivelyVisible);
        Assert.True(title.IsFocused);
        Assert.False(add.IsEffectivelyEnabled);
        var labels = VisibleTexts(form);
        Assert.All(new[] { "Новый параметр", "Название", "SignalId", "Направление", "Тип значения", "Отображать для" },
            label => Assert.Contains(label, labels));
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
        Assert.Equal(scroll.Extent.Width, scroll.Viewport.Width, precision: 1);
        var cancel = FindButton(dialog, "Отмена");
        cancel.BringIntoView();
        Assert.True(cancel.Focus());
        fixture.PressSpace();
        Assert.False(form.IsEffectivelyVisible);
        Assert.True(add.IsEffectivelyEnabled);
        Assert.Equal(parameterCount > 0, FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        Assert.Equal(parameterCount, fixture.Manager.CurrentDocument.Cards.Single().Parameters.Count);
        await fixture.CloseAsync(dialog);
    }

    [AvaloniaFact]
    public async Task Admin_creates_bool_inline_and_can_save_explicit_false_after_reopening()
    {
        using var fixture = new DialogFixture(ApplicationOptions.AdminWorkMode, offline: true, parameterCount: 0);
        var dialog = fixture.Open();
        var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
        fixture.Click(FindButton(dialog, "Добавить"));
        var form = dialog.FindControl<Border>("NewParameterForm")!;
        var fields = form.GetVisualDescendants().OfType<ComboBox>().ToArray();
        Assert.Equal(SignalBindingDirection.ReadWrite, fields[0].SelectedItem);
        Assert.Equal(SignalValueType.Word, fields[1].SelectedItem);
        Assert.Equal(EquipmentParameterVisibility.User, fields[2].SelectedValue);
        fields[0].SetCurrentValue(SelectingItemsControl.SelectedItemProperty, SignalBindingDirection.Write);
        fields[1].SetCurrentValue(SelectingItemsControl.SelectedItemProperty, SignalValueType.Bool);
        fields[2].SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 1);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);

        var create = FindButton(dialog, "Создать");
        fixture.Click(create);
        Assert.Contains("Название настройки оборудования обязательно.", VisibleTexts(dialog));
        var error = dialog.FindControl<TextBlock>("CreateErrorText")!;
        var scroll = dialog.FindControl<ScrollViewer>("ParametersScrollViewer")!;
        Assert.True(error.TranslatePoint(default, dialog)!.Value.Y >=
            scroll.TranslatePoint(new Point(0, scroll.Bounds.Height), dialog)!.Value.Y);
        Assert.True(form.IsEffectivelyVisible);
        dialog.FindControl<TextBox>("NewParameterTitleBox")!.SetCurrentValue(TextBox.TextProperty, "Разрешение");
        dialog.FindControl<TextBox>("NewParameterSignalIdBox")!.SetCurrentValue(TextBox.TextProperty, "test.parameter.bool");
        var created = viewModel.CreateCommand.FirstAsync().ToTask();
        fixture.Click(create);
        await created.WaitAsync(TestTimeout);
        Dispatcher.UIThread.RunJobs();
        Assert.False(form.IsEffectivelyVisible);
        Assert.Contains("Настройка добавлена", VisibleTexts(dialog));
        Assert.Equal("test.parameter.bool", Assert.Single(viewModel.Parameters).SignalId);
        Assert.True(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        var persisted = Assert.Single(fixture.Storage.LoadActive()!.Cards.Single().Parameters);
        Assert.Equal((SignalBindingRole.EquipmentParameter, SignalBindingDirection.Write, SignalValueType.Bool,
            EquipmentParameterVisibility.Admin, null, false, null),
            (persisted.Role, persisted.Direction, persisted.ValueType, persisted.Visibility,
                persisted.SavedValue, persisted.PendingAutoDispatch, persisted.LastDispatchError));
        Assert.Empty(fixture.Store.Saves);
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);

        dialog = fixture.Open(ApplicationOptions.UserWorkMode);
        Assert.Empty(Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext).Parameters);
        Assert.False(FindButton(dialog, "Добавить").IsEffectivelyVisible);
        await fixture.CloseAsync(dialog);

        dialog = fixture.Open();
        var boolChoice = Assert.Single(dialog.GetVisualDescendants().OfType<ComboBox>(),
            box => box.DataContext is EquipmentCardParameterRow && box.IsEffectivelyVisible);
        Assert.Equal(-1, boolChoice.SelectedIndex);
        await SaveOperatorDialogAsync(fixture, dialog);
        Assert.Empty(fixture.Store.Saves);
        boolChoice.SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 0);
        await SaveOperatorDialogAsync(fixture, dialog);
        var save = Assert.Single(fixture.Store.Saves);
        Assert.True(save.Pending);
        Assert.Equal(new EquipmentParameterSetpoint("test.parameter.bool", "false"), Assert.Single(save.Values));
        Assert.Empty(fixture.Dispatcher.Requests);
        await fixture.CloseAsync(dialog);
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
        Assert.Empty(ParameterList(dialog).Items);
        Assert.False(FindButton(dialog, "Сохранить").IsEffectivelyEnabled);
        Assert.True(FindButton(dialog, "Закрыть").IsEffectivelyEnabled);

        await fixture.CloseAsync(dialog);
        var adminDialog = fixture.Open(ApplicationOptions.AdminWorkMode);
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId },
            Assert.IsType<EquipmentCardParametersDialogViewModel>(adminDialog.DataContext).Parameters.Select(row => row.SignalId));
        await fixture.CloseAsync(adminDialog);
        Assert.Equal(originalJson, File.ReadAllText(fixture.Storage.ActiveFilePath));
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Empty(fixture.Store.Saves);
    }

    // UI-10
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Editor_and_operator_dialogs_preserve_hidden_setpoints_online_and_offline(bool offline)
    {
        using var fixture = new DialogFixture(ApplicationOptions.UserWorkMode, offline: offline, adminInitiallyVisible: true);
        await SetAdminVisibilityInEditorAsync(fixture);
        Assert.Empty(fixture.Dispatcher.Requests);
        Assert.Empty(fixture.Store.Saves);

        var originalParameters = fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters;
        var originalInventory = RouteMapSignalInventory.Build(fixture.Manager.CurrentDefinition);
        Assert.All(new[] { UserSignalId, AdminSignalId, ReadSignalId },
            id => Assert.Contains(originalInventory, item => item.SignalId == id));
        var adminDialog = fixture.Open(ApplicationOptions.AdminWorkMode);
        var adminViewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(adminDialog.DataContext);
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId }, adminViewModel.Parameters.Select(row => row.SignalId));
        Assert.True(Assert.Single(adminDialog.GetVisualDescendants().OfType<TextBox>(),
            box => box.DataContext is EquipmentCardParameterRow row && row.SignalId == ReadSignalId).IsReadOnly);
        await fixture.CloseAsync(adminDialog);

        var expectedJson = JsonNode.Parse(File.ReadAllText(fixture.Storage.ActiveFilePath))!;
        var userParameterJson = expectedJson["cards"]!.AsArray().Single()!["parameters"]!.AsArray()
            .Single(parameter => (string?)parameter!["signalId"] == UserSignalId)!;
        userParameterJson["savedValue"] = "42";
        userParameterJson["pendingAutoDispatch"] = offline;
        var dialog = fixture.Open();
        var input = Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(),
            box => box.IsEffectivelyVisible && !box.IsReadOnly);
        var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
        Assert.Equal(new[] { UserSignalId, ReadSignalId }, viewModel.Parameters.Select(row => row.SignalId));
        Assert.Equal("100", input.Text);
        input.SetCurrentValue(TextBox.TextProperty, "42");
        Assert.Equal("42", viewModel.Parameters[0].ValueText);

        await SaveOperatorDialogAsync(fixture, dialog);
        var save = Assert.Single(fixture.Store.Saves);
        Assert.Equal(offline, save.Pending);
        Assert.Equal(new EquipmentParameterSetpoint(UserSignalId, "42"), Assert.Single(save.Values));
        if (offline)
            Assert.Empty(fixture.Dispatcher.Requests);
        else
        {
            var request = Assert.Single(fixture.Dispatcher.Requests);
            Assert.Equal((UserSignalId, (ushort)42, SignalValueType.UInt16), (request.SignalId, request.Value, request.ValueType));
        }

        Assert.Equal(new[] { originalParameters[0] with { SavedValue = "42", PendingAutoDispatch = offline },
                originalParameters[1], originalParameters[2] }, fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters);
        var hidden = Assert.Single(fixture.Manager.CurrentDocument.Cards.Single().Parameters,
            parameter => parameter.SignalId == AdminSignalId);
        Assert.Equal((EquipmentParameterVisibility.Admin, "5000", true, "Предыдущая ошибка"),
            (hidden.Visibility, hidden.SavedValue, hidden.PendingAutoDispatch, hidden.LastDispatchError));
        Assert.Equal(originalParameters[1], Assert.Single(fixture.Store.GetPending(),
            item => item.Parameter.Binding.SignalId == AdminSignalId).Parameter);
        Assert.Equal(originalInventory, RouteMapSignalInventory.Build(fixture.Manager.CurrentDefinition));
        Assert.True(JsonNode.DeepEquals(expectedJson, JsonNode.Parse(File.ReadAllText(fixture.Storage.ActiveFilePath))));

        await fixture.CloseAsync(dialog);
        adminDialog = fixture.Open(ApplicationOptions.AdminWorkMode);
        adminViewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(adminDialog.DataContext);
        Assert.Equal(new[] { UserSignalId, AdminSignalId, ReadSignalId }, adminViewModel.Parameters.Select(row => row.SignalId));
        Assert.Equal("5000", adminViewModel.Parameters[1].ValueText);
        Assert.Equal("Предыдущая ошибка", adminViewModel.Parameters[1].DispatchError);
        Assert.Equal(offline ? 0 : 1, fixture.Dispatcher.Requests.Count);
        Assert.Single(fixture.Store.Saves);

        adminViewModel.Parameters[1].ValueText = "5001";
        await SaveOperatorDialogAsync(fixture, adminDialog);
        Assert.Equal(new[] { new EquipmentParameterSetpoint(UserSignalId, "42"), new EquipmentParameterSetpoint(AdminSignalId, "5001") },
            fixture.Store.Saves[1].Values);
        Assert.Equal(offline ? [] : new[] { UserSignalId, UserSignalId, AdminSignalId },
            fixture.Dispatcher.Requests.Select(request => request.SignalId));
        await fixture.CloseAsync(adminDialog);
    }

    private static async Task SetAdminVisibilityInEditorAsync(DialogFixture fixture)
    {
        using var editor = new RouteMapSettingsViewModel(fixture.Manager, fixture.Storage, new NullFilePicker());
        var view = new RouteMapSettingsDialog { DataContext = editor };
        var window = new Window { Width = 1320, Height = 780, Content = view };
        try
        {
            window.Show();
            Assert.Single(view.GetVisualDescendants().OfType<TabControl>()).SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            var parameters = editor.SelectedCard!.Parameters;
            var choices = view.GetVisualDescendants().OfType<ComboBox>()
                .Where(box => box.DataContext is EquipmentCardParameterConfiguration && Grid.GetColumn(box) == 6)
                .ToArray();
            Assert.Equal(3, choices.Length);
            Assert.All(choices, box => Assert.Equal(EquipmentParameterVisibility.User, box.SelectedValue));
            Assert.Single(choices, box => ReferenceEquals(box.DataContext, parameters[1]))
                .SetCurrentValue(SelectingItemsControl.SelectedIndexProperty, 1);
            editor.ApplyCommand.Execute().Subscribe();
            Assert.Null(editor.ErrorMessage);
            Assert.Equal(EquipmentParameterVisibility.Admin,
                fixture.Manager.CurrentDefinition.MapEquipment.Single().Parameters[1].Visibility);
            Assert.Equal(EquipmentParameterVisibility.User, fixture.Storage.LoadActive()!.Cards.Single().Parameters[1].Visibility);
            await editor.SaveCommand.Execute().ToTask().WaitAsync(TestTimeout);
            Assert.Null(editor.ErrorMessage);
            var reloaded = await fixture.Manager.LoadActiveDraftAsync();
            Assert.Equal(new[] { EquipmentParameterVisibility.User, EquipmentParameterVisibility.Admin, EquipmentParameterVisibility.User },
                reloaded.Cards.Single().Parameters.Select(parameter => parameter.Visibility));
            Assert.True(fixture.Manager.Apply(reloaded).IsSuccess);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task SaveOperatorDialogAsync(DialogFixture fixture, EquipmentCardParametersDialogView dialog)
    {
        var viewModel = Assert.IsType<EquipmentCardParametersDialogViewModel>(dialog.DataContext);
        var saveCompleted = viewModel.SaveCommand.FirstAsync().ToTask();
        fixture.Click(FindButton(dialog, "Сохранить"));
        await saveCompleted.WaitAsync(TestTimeout);
        Assert.Null(viewModel.ErrorMessage);
    }

    private static string?[] VisibleTexts(Control dialog) => dialog.GetVisualDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible)
        .Select(text => text.Text)
        .ToArray();

    private static Button FindButton(Control dialog, string content) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, content));

    private static ItemsControl ParameterList(Control dialog) =>
        Assert.Single(dialog.GetVisualDescendants().OfType<ItemsControl>(), items => items.GetType() == typeof(ItemsControl));

    private sealed class DialogFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"equipment-dialog-{Guid.NewGuid():N}");
        private readonly DialogHostStyles _hostStyles = new();
        private readonly ServiceProvider _services;
        private readonly DialogHost _host;
        private readonly Window _window;
        private readonly string _workMode;
        private readonly bool _offline;

        public DialogFixture(string workMode, bool allAdmin = false, bool offline = false,
            bool adminInitiallyVisible = false, int parameterCount = 3)
        {
            _workMode = workMode;
            _offline = offline;
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
                SavedValue = "100",
            });
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Фильтер",
                SignalId = AdminSignalId,
                ValueType = SignalValueType.UInt16,
                Visibility = adminInitiallyVisible ? EquipmentParameterVisibility.User : EquipmentParameterVisibility.Admin,
                SavedValue = "5000",
                PendingAutoDispatch = true,
                LastDispatchError = "Предыдущая ошибка",
            });
            bucket.Parameters.Add(new EquipmentCardParameterConfiguration
            {
                Title = "Текущий вес",
                SignalId = ReadSignalId,
                Direction = SignalBindingDirection.Read,
                ValueType = SignalValueType.UInt16,
                Visibility = allAdmin ? EquipmentParameterVisibility.Admin : EquipmentParameterVisibility.User,
            });
            if (parameterCount == 0)
                bucket.Parameters.Clear();
            for (var index = bucket.Parameters.Count; index < parameterCount; index++)
            {
                bucket.Parameters.Add(new EquipmentCardParameterConfiguration
                {
                    Title = $"Параметр {index}",
                    SignalId = $"test.parameter.extra.{index}",
                    Direction = SignalBindingDirection.Read,
                });
            }
            Storage.SaveActive(document);
            Manager = new RouteMapConfigurationManager(
                Storage, mapper, new RouteMapConfigurationValidator(), new RouteMapConfigurationMigrator());
            Assert.Null(Manager.LastLoadError);
            Store = new RecordingValueStore(Manager, Options.Create(new ApplicationOptions { WorkMode = workMode }));
            _services = new ServiceCollection()
                .AddSingleton<IEquipmentCommandDispatcher>(Dispatcher)
                .AddSingleton<IEquipmentParameterWriteValidator>(NoopEquipmentParameterWriteValidator.Instance)
                .AddSingleton<IEquipmentParameterValueStore>(Store)
                .AddSingleton<IEquipmentCardParameterDefinitionStore>(Store)
                .AddTransient<EquipmentCardParametersDialogView>()
                .BuildServiceProvider();
            Avalonia.Application.Current!.Styles.Add(_hostStyles);
            _host = new DialogHost
            {
                Identifier = DialogHostIds.Root,
                IsMultipleDialogsEnabled = true,
            };
            _window = new Window { Width = 700, Height = 480, Content = _host };
            _window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public RouteMapConfigurationStorage Storage { get; }
        public RouteMapConfigurationManager Manager { get; }
        public RecordingValueStore Store { get; }
        public RecordingDispatcher Dispatcher { get; } = new();

        public EquipmentCardParametersDialogView Open(string? workMode = null)
        {
            var dialogService = new EquipmentCardParametersDialogService(
                _services, new DialogCoordinator(), Options.Create(new ApplicationOptions { WorkMode = workMode ?? _workMode }));
            var card = Manager.CurrentDefinition.MapEquipment.Single();
            var cardViewModel = new EquipmentCardViewModel(card, Dispatcher, parametersDialogService: dialogService);
            cardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
                card.Id, _offline ? RouteObjectState.Offline : RouteObjectState.Idle, card.StatusText, ValueText: null,
                IsVisible: true, CanStart: false, CanStop: false, IsEnabled: !_offline), isConnectionAvailable: !_offline);
            var cardView = new EquipmentCardView { DataContext = cardViewModel };
            _host.Content = cardView;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var button = cardView.FindControl<Button>("ParametersButton")!;
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
            button.BringIntoView();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var center = new Point(button.Bounds.Width / 2, button.Bounds.Height / 2);
            var point = button.TranslatePoint(center, _window)!.Value;
            _window.MouseMove(point, RawInputModifiers.None);
            _window.MouseDown(point, MouseButton.Left, RawInputModifiers.None);
            _window.MouseUp(point, MouseButton.Left, RawInputModifiers.None);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public void PressSpace()
        {
            _window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            _window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            if (_host.IsOpen)
                DialogHost.Close(DialogHostIds.Root, false);
            _window.Close();
            Avalonia.Application.Current!.Styles.Remove(_hostStyles);
            _services.Dispose();
            Store.Dispose();
            Manager.Dispose();
            Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class RecordingValueStore(RouteMapConfigurationManager manager, IOptions<ApplicationOptions> options)
        : IEquipmentParameterValueStore, IEquipmentCardParameterDefinitionStore, IDisposable
    {
        private readonly RouteMapEquipmentParameterValueStore _store = new(manager, options);
        public List<(EquipmentParameterSetpoint[] Values, bool Pending)> Saves { get; } = [];

        public Task SaveAsync(string cardId, IReadOnlyCollection<EquipmentParameterSetpoint> values,
            bool pendingAutoDispatch, CancellationToken cancellationToken = default)
        {
            Saves.Add((values.ToArray(), pendingAutoDispatch));
            return _store.SaveAsync(cardId, values, pendingAutoDispatch, cancellationToken);
        }

        public IReadOnlyList<PendingEquipmentParameter> GetPending() => _store.GetPending();

        public Task<EquipmentCardParameter> AddAsync(string cardId, NewEquipmentCardParameterDefinition definition,
            CancellationToken cancellationToken = default) => _store.AddAsync(cardId, definition, cancellationToken);

        public Task CompleteDispatchAsync(string cardId, string signalId, string expectedSavedValue,
            string? errorMessage, CancellationToken cancellationToken = default) =>
            _store.CompleteDispatchAsync(cardId, signalId, expectedSavedValue, errorMessage, cancellationToken);

        public void Dispose() => _store.Dispose();
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
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
