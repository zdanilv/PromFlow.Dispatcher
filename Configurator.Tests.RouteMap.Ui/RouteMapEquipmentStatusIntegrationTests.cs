using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed partial class RouteMapSettingsDialogVisualTests
{
    [AvaloniaFact]
    public async Task Migrated_bucket_renders_all_mock_codes_and_keeps_route_and_power_independent()
    {
        using var fixture = new MigratedStatusFixture();
        Assert.Equal(19, fixture.Manager.CurrentDocument.SchemaVersion);
        Assert.True(File.Exists(fixture.Storage.ActiveFilePath + ".bak"));
        Assert.Equal("Кюбель Л.К.", fixture.Card.Title);
        var binding = fixture.Manager.CurrentDefinition.MapEquipment.Single().Bindings
            .Single(item => item.Role == SignalBindingRole.Text);
        Assert.Equal((MigratedStatusFixture.TextId, SignalBindingDirection.Read, SignalValueType.UInt16),
            (binding.SignalId, binding.Direction, binding.ValueType));

        var rows = new (ushort Code, string Text, string Color, EquipmentStatusCategory Category)[]
        {
            (0, "Выключен", "#112233", EquipmentStatusCategory.Normal),
            (1, "Ожидание", "#99445566", EquipmentStatusCategory.Information),
            (2, "Авария", "#778899", EquipmentStatusCategory.Fault),
            (3, "Выполнение", "#126534", EquipmentStatusCategory.Information),
            (4, "Выгрузка", "#126534", EquipmentStatusCategory.Information),
            (5, "Загрузка", "#126534", EquipmentStatusCategory.Information),
        };
        var updatedOnUiThread = new List<bool>();
        fixture.Card.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EquipmentCardViewModel.StatusText))
                updatedOnUiThread.Add(Dispatcher.UIThread.CheckAccess());
        };
        foreach (var row in rows)
        {
            var snapshot = fixture.Mock.CreateSnapshot(row.Code).ToDictionary();
            Assert.Equal(row.Code, Assert.IsType<ushort>(snapshot[MigratedStatusFixture.TextId].Value));
            SetSignal(snapshot, MigratedStatusFixture.PowerId, row.Code is 0 or 2);
            await Task.Run(() => fixture.Signals.OnNext(snapshot));
            Dispatcher.UIThread.RunJobs();
            var allowed = row.Category != EquipmentStatusCategory.Fault;
            fixture.AssertStatus(row.Code, row.Text, row.Color, row.Category, EquipmentStatusQuality.Known,
                allowed, allowed);
            Assert.Equal(Color.Parse(row.Code is 0 or 2 ? "#00D107" : "#D10000"), fixture.PowerColor);
        }
        Assert.Equal(rows.Length, updatedOnUiThread.Count);
        Assert.All(updatedOnUiThread, updated => Assert.True(updated));

        var fixedRoute = fixture.Mock.CreateSnapshot(0).ToDictionary();
        fixture.Publish(fixedRoute);
        var activeBefore = fixture.Dashboard.RuntimeState.Objects.Values
            .Where(item => item.IsSignalActive).Select(item => item.ObjectId).ToArray();
        Assert.NotEmpty(activeBefore);
        SetText(fixedRoute, (ushort)3);
        fixture.Publish(fixedRoute);
        Assert.Equal(activeBefore, fixture.Dashboard.RuntimeState.Objects.Values
            .Where(item => item.IsSignalActive).Select(item => item.ObjectId));

        fixedRoute.Remove(MigratedStatusFixture.PowerId);
        fixture.Publish(fixedRoute);
        fixture.AssertStatus(3, "Выполнение", "#126534", EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, true, true);
        Assert.Equal(Color.Parse("#3F474D"), fixture.PowerColor);

        foreach (var stale in new[] { false, true })
        {
            SetSignal(fixedRoute, MigratedStatusFixture.PowerId, true, isGood: stale, isStale: stale);
            fixture.Publish(fixedRoute);
            Assert.Null(fixture.Runtime.IsPowerOn);
            Assert.Equal(Color.Parse("#3F474D"), fixture.PowerColor);
            // Bad quality on another bound signal still applies the existing card-wide offline rule.
            fixture.AssertStatus(null, "Не в сети", fixture.Palette.Offline, null,
                EquipmentStatusQuality.Disconnected, false, false);
        }
        Assert.False(fixture.CardView.FindControl<Border>("PowerIndicator")!.Focusable);
        Assert.Empty(fixture.Commands.Requests);
    }

    [AvaloniaFact]
    public async Task Migrated_bucket_uses_editor_updates_and_independent_card_tables_without_writes()
    {
        using var fixture = new MigratedStatusFixture();
        Assert.False(fixture.Dashboard.TopBar.IsSettingsVisible);
        var active = fixture.Storage.ActiveFilePath;
        var persisted = await File.ReadAllTextAsync(active);
        var export = active + ".export";
        using var editor = new RouteMapSettingsViewModel(fixture.Manager, fixture.Storage, new StatusFilePicker(active + ".bak", export));
        var published = fixture.Manager.CurrentDefinition;
        var commandTimeout = TimeSpan.FromSeconds(10);
        await editor.ImportCommand.Execute().ToTask().WaitAsync(commandTimeout);
        Assert.Same(published, fixture.Manager.CurrentDefinition);
        Assert.Equal(19, editor.Draft.SchemaVersion);

        var dialog = new RouteMapSettingsDialog { DataContext = editor };
        var window = new Window { Width = 1320, Height = 780, Content = dialog };
        window.Show();
        try
        {
            dialog.GetVisualDescendants().OfType<TabControl>().Single().SelectedIndex = 5;
            Dispatcher.UIThread.RunJobs();
            var bucket = editor.SelectedCard!;
            Assert.Equal(MigratedStatusFixture.BucketId, bucket.Id);
            var entries = bucket.StatusMapping!.Entries;
            entries[1].BlockStart = true;
            entries[0].BlockStop = true;
            entries[3].BlockStart = entries[3].BlockStop = true;

            var add = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Добавить статус"));
            add.Command!.Execute(add.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            var row = dialog.GetVisualDescendants().OfType<Grid>().Last(grid => grid.Classes.Contains("status-row"));
            var fields = row.GetVisualDescendants().OfType<TextBox>().ToArray();
            fields[0].SetCurrentValue(TextBox.TextProperty, "42");
            fields[1].SetCurrentValue(TextBox.TextProperty, "Подготовка ✓");
            fields[2].SetCurrentValue(TextBox.TextProperty, "#FF126534");
            row.Children.OfType<ComboBox>().Single().SetCurrentValue(
                SelectingItemsControl.SelectedItemProperty, EquipmentStatusCategory.Warning);
            Dispatcher.UIThread.RunJobs();

            var snapshot = fixture.Mock.CreateSnapshot(3).ToDictionary();
            SetText(snapshot, 42);
            fixture.Publish(snapshot);
            fixture.AssertStatus(42, "Неизвестный статус (код 42)", fixture.Palette.Warning,
                EquipmentStatusCategory.Warning, EquipmentStatusQuality.UnknownCode, false, false);
            Assert.Same(published, fixture.Manager.CurrentDefinition);
            Assert.Equal(persisted, await File.ReadAllTextAsync(active));

            editor.DuplicateCardCommand.Execute().Subscribe();
            var second = editor.SelectedCard!;
            Assert.NotSame(entries, second.StatusMapping!.Entries);
            var secondEntry = second.StatusMapping.Entries.Single(item => item.Code == 3);
            Assert.NotSame(entries[3], secondEntry);
            secondEntry.Text = "Статус второй карточки";
            secondEntry.Color = "#581234";
            secondEntry.BlockStart = secondEntry.BlockStop = false;
            second.CanStart = second.CanStop = false;
            editor.SelectedCard = bucket;
            editor.DuplicateCardCommand.Execute().Subscribe();
            var legacy = editor.SelectedCard!;
            legacy.UseStatusMapping = false;
            Assert.Equal("Выполнение", entries[3].Text);
            Assert.Empty(fixture.Commands.Requests);

            var oldCard = fixture.Card;
            editor.Apply();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(editor.ErrorMessage);
            Assert.NotSame(oldCard, fixture.Card);
            Assert.Equal(persisted, await File.ReadAllTextAsync(active));
            fixture.AssertStatus(42, "Подготовка ✓", "#FF126534", EquipmentStatusCategory.Warning,
                EquipmentStatusQuality.Known, true, true);
            Assert.Empty(fixture.Commands.Requests);

            SetText(snapshot, 1);
            fixture.Publish(snapshot);
            fixture.AssertStatus(1, "Ожидание", "#99445566", EquipmentStatusCategory.Information,
                EquipmentStatusQuality.Known, false, true);
            Assert.True(fixture.Card.StatusPresentation!.BlockStart);
            Assert.False(fixture.Card.StatusPresentation.BlockStop);
            SetSignal(snapshot, MigratedStatusFixture.StopOffId, true);
            fixture.Publish(snapshot);
            Assert.False(fixture.Card.CanStop);
            SetSignal(snapshot, MigratedStatusFixture.StopOffId, false);
            SetText(snapshot, 0);
            fixture.Publish(snapshot);
            fixture.AssertStatus(0, "Выключен", "#112233", EquipmentStatusCategory.Normal,
                EquipmentStatusQuality.Known, true, false);
            Assert.False(fixture.Card.StatusPresentation!.BlockStart);
            Assert.True(fixture.Card.StatusPresentation.BlockStop);
            SetSignal(snapshot, MigratedStatusFixture.StartOffId, true);
            fixture.Publish(snapshot);
            Assert.False(fixture.Card.CanStart);

            SetText(snapshot, 3);
            foreach (var card in new[] { second, legacy })
            {
                var id = card.Bindings.Single(binding => binding.Role == SignalBindingRole.Text).SignalId;
                snapshot[id] = new(id, (ushort)3, SignalValueType.UInt16, DateTimeOffset.UnixEpoch, true, false);
            }
            SetSignal(snapshot, MigratedStatusFixture.StartOffId, false);
            fixture.Publish(snapshot);
            fixture.AssertStatus(3, "Выполнение", "#126534", EquipmentStatusCategory.Information,
                EquipmentStatusQuality.Known, false, false);
            var secondCard = fixture.Dashboard.MapEquipmentCards.Single(card => card.Id == second.Id);
            Assert.Equal("Статус второй карточки", secondCard.StatusText);
            Assert.Equal("#581234", secondCard.StatusPresentation!.Color);
            Assert.False(secondCard.StatusPresentation.BlockStart);
            Assert.False(secondCard.StatusPresentation.BlockStop);
            Assert.False(secondCard.CanStart);
            Assert.False(secondCard.CanStop);
            var legacyCard = fixture.Dashboard.MapEquipmentCards.Single(card => card.Id == legacy.Id);
            Assert.Equal("Выполнение", legacyCard.StatusText);
            Assert.Null(legacyCard.StatusPresentation);
            Assert.True(legacyCard.CanStart);
            Assert.True(legacyCard.CanStop);

            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
                await editor.SaveAsync().WaitAsync(commandTimeout);
                await editor.ExportCommand.Execute().ToTask().WaitAsync(commandTimeout);
                Assert.Null(editor.ErrorMessage);
                using var json = JsonDocument.Parse(await File.ReadAllTextAsync(active));
                var savedEntry = json.RootElement.GetProperty("cards")[0].GetProperty("statusMapping")
                    .GetProperty("entries").EnumerateArray().Single(item => item.GetProperty("code").GetInt32() == 42);
                Assert.Equal("warning", savedEntry.GetProperty("category").GetString());
                Assert.Equal("Подготовка ✓", savedEntry.GetProperty("text").GetString());
                Assert.Equal("#FF126534", savedEntry.GetProperty("color").GetString());

                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                using var reopened = fixture.OpenManager();
                Assert.Null(reopened.LastLoadError);
                Assert.Equal(19, reopened.CurrentDocument.SchemaVersion);
                var reopenedMapping = reopened.CurrentDefinition.MapEquipment.Single(card => card.Id == bucket.Id).StatusMapping!;
                Assert.Equal(new EquipmentStatusEntry(42, "Подготовка ✓", "#FF126534",
                    EquipmentStatusCategory.Warning, false, false), reopenedMapping.Entries.Single(item => item.Code == 42));
                Assert.Equal(19, (await reopened.ImportDraftAsync(export)).SchemaVersion);
                Assert.Equal(3, reopened.CurrentDocument.Cards.Count);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            editor.SelectedCard = second;
            editor.DeleteCardCommand.Execute().Subscribe();
            editor.Apply();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(editor.ErrorMessage);
            Assert.Equal(new[] { bucket.Id, legacy.Id }, fixture.Dashboard.MapEquipmentCards.Select(card => card.Id));
            SetText(snapshot, 42);
            fixture.Publish(snapshot);
            fixture.AssertStatus(42, "Подготовка ✓", "#FF126534", EquipmentStatusCategory.Warning,
                EquipmentStatusQuality.Known, true, true);
            Assert.Equal("Неизвестный статус (код 42)", oldCard.StatusText);
            Assert.Equal(1, fixture.Subscriptions);
            Assert.Empty(fixture.Commands.Requests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Migrated_bucket_renders_unknown_unavailable_fault_disabled_and_reconnect_without_writes()
    {
        using var fixture = new MigratedStatusFixture();
        fixture.Publish(new Dictionary<string, SignalValue>());
        fixture.AssertStatus(null, "Нет достоверного статуса", "#112233", null,
            EquipmentStatusQuality.Missing, false, false);

        var snapshot = fixture.Mock.CreateSnapshot(3).ToDictionary();
        foreach (ushort code in new ushort[] { 6, 9, ushort.MaxValue })
        {
            SetText(snapshot, code);
            fixture.Publish(snapshot);
            fixture.AssertStatus(code, $"Неизвестный статус (код {code})", fixture.Palette.Warning,
                EquipmentStatusCategory.Warning, EquipmentStatusQuality.UnknownCode, false, false);
            Assert.Contains("Код отсутствует в таблице", fixture.Card.StatusToolTip);
        }

        var unavailable = new (SignalValue? Signal, EquipmentStatusQuality Quality, string Reason)[]
        {
            (null, EquipmentStatusQuality.Missing, "Значение Text отсутствует"),
            (TextSignal(null), EquipmentStatusQuality.WrongType, "(null)"),
            (TextSignal((ushort)3, isGood: false), EquipmentStatusQuality.BadOrStale, "Недостоверное значение Text"),
            (TextSignal((ushort)3, isStale: true), EquipmentStatusQuality.BadOrStale, "Значение Text устарело"),
            (TextSignal(true, SignalValueType.Bool), EquipmentStatusQuality.WrongType, "(Boolean)"),
            (TextSignal(-1), EquipmentStatusQuality.WrongType, "(Int32)"),
            (TextSignal(65536u), EquipmentStatusQuality.WrongType, "(UInt32)"),
            (TextSignal("три"), EquipmentStatusQuality.WrongType, "(String)"),
            (TextSignal(1.5), EquipmentStatusQuality.WrongType, "(Double)"),
        };
        foreach (var row in unavailable)
        {
            SetText(snapshot, (ushort)3);
            fixture.Publish(snapshot);
            Assert.Equal("Выполнение", fixture.Card.StatusText);
            if (row.Signal is null)
                snapshot.Remove(MigratedStatusFixture.TextId);
            else
                snapshot[MigratedStatusFixture.TextId] = row.Signal;
            fixture.Publish(snapshot);
            fixture.AssertStatus(null, "Нет достоверного статуса", "#112233", null, row.Quality, false, false);
            Assert.Contains(row.Reason, fixture.Card.StatusToolTip);
        }

        SetText(snapshot, (ushort)3);
        SetSignal(snapshot, RouteMapSystemSignalIds.GlobalFault, true);
        fixture.Publish(snapshot);
        fixture.AssertStatus(3, "Авария системы", fixture.Palette.Fault, EquipmentStatusCategory.Fault,
            EquipmentStatusQuality.ForcedFault, false, false);
        SetSignal(snapshot, RouteMapSystemSignalIds.GlobalFault, false);
        SetSignal(snapshot, MigratedStatusFixture.FaultId, true);
        fixture.Publish(snapshot);
        fixture.AssertStatus(3, "Авария оборудования", fixture.Palette.Fault, EquipmentStatusCategory.Fault,
            EquipmentStatusQuality.ForcedFault, false, false);
        SetSignal(snapshot, MigratedStatusFixture.FaultId, false);
        SetSignal(snapshot, MigratedStatusFixture.EnabledId, false);
        fixture.Publish(snapshot);
        fixture.AssertStatus(3, "Выполнение", fixture.Palette.Disabled, EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, false, false);
        Assert.Equal(RouteObjectState.Disabled, fixture.Card.State);
        SetText(snapshot, (ushort)2);
        fixture.Publish(snapshot);
        fixture.AssertStatus(2, "Авария", "#778899", EquipmentStatusCategory.Fault,
            EquipmentStatusQuality.Known, false, false);
        Assert.Equal(RouteObjectState.Fault, fixture.Card.State);
        SetSignal(snapshot, RouteMapSystemSignalIds.GlobalFault, true);
        fixture.Publish(snapshot);
        fixture.AssertStatus(2, "Авария системы", fixture.Palette.Fault, EquipmentStatusCategory.Fault,
            EquipmentStatusQuality.ForcedFault, false, false);

        SetSignal(snapshot, RouteMapSystemSignalIds.GlobalFault, false);
        SetSignal(snapshot, MigratedStatusFixture.EnabledId, true);
        SetText(snapshot, (ushort)3);
        fixture.Publish(snapshot);
        fixture.AssertStatus(3, "Выполнение", "#126534", EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, true, true);
        SetSignal(snapshot, RouteMapSystemSignalIds.ConnectionConnected, false);
        fixture.Publish(snapshot);
        fixture.AssertStatus(null, "Не в сети", fixture.Palette.Offline, null,
            EquipmentStatusQuality.Disconnected, false, false);
        Assert.Equal(RouteObjectState.Offline, fixture.Card.State);
        SetSignal(snapshot, RouteMapSystemSignalIds.ConnectionConnected, true);
        snapshot.Remove(MigratedStatusFixture.TextId);
        fixture.Publish(snapshot);
        fixture.AssertStatus(null, "Нет достоверного статуса", "#112233", null,
            EquipmentStatusQuality.Missing, false, false);
        SetText(snapshot, (ushort)1);
        fixture.Publish(snapshot);
        fixture.AssertStatus(1, "Ожидание", "#99445566", EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, true, true);
        var parameterAttempts = fixture.Parameters.Attempts;
        fixture.Signals.OnNext(null!);
        Dispatcher.UIThread.RunJobs();
        fixture.AssertStatus(null, "Не в сети", fixture.Palette.Offline, null,
            EquipmentStatusQuality.Disconnected, false, false);
        fixture.Publish(snapshot);
        fixture.AssertStatus(1, "Ожидание", "#99445566", EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, true, true);
        Assert.Equal(parameterAttempts, fixture.Parameters.Attempts);
        Assert.Empty(fixture.Commands.Requests);
    }

    [AvaloniaFact]
    public void Dashboard_ignores_queued_definition_and_live_snapshot_after_disposal()
    {
        using var fixture = new MigratedStatusFixture();
        var definition = fixture.Dashboard.Definition;
        var runtime = fixture.Dashboard.RuntimeState;
        var draft = fixture.Manager.CreateDraft();
        draft.Cards.Single().Title = "Новая подпись";
        Assert.True(fixture.Manager.Apply(draft).IsSuccess);
        fixture.Signals.OnNext(fixture.Mock.CreateSnapshot(3));

        fixture.DisposeDashboard();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, fixture.Parameters.Attempts);
        Assert.Empty(fixture.Commands.Requests);
        Assert.Same(definition, fixture.Dashboard.Definition);
        Assert.Same(runtime, fixture.Dashboard.RuntimeState);
    }

    [AvaloniaFact]
    public void Switching_to_modbus_discards_a_queued_mock_snapshot_until_the_first_modbus_poll()
    {
        using var fixture = new MigratedStatusFixture();
        fixture.Signals.OnNext(fixture.Mock.CreateSnapshot(3));
        fixture.SwitchToModbus();
        Dispatcher.UIThread.RunJobs();

        fixture.AssertStatus(null, "Не в сети", fixture.Palette.Offline, null,
            EquipmentStatusQuality.Disconnected, false, false);
        Assert.Equal(0, fixture.Parameters.Attempts);
        Assert.Empty(fixture.Commands.Requests);

        fixture.ModbusSignals.OnNext(fixture.Mock.CreateSnapshot(3));
        Dispatcher.UIThread.RunJobs();
        fixture.AssertStatus(3, "Выполнение", "#126534", EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, true, true);
        Assert.Equal(1, fixture.Parameters.Attempts);
        Assert.Empty(fixture.Commands.Requests);
    }

    [AvaloniaFact]
    public void Switching_between_healthy_sources_does_not_trigger_pending_parameter_dispatch()
    {
        using var fixture = new MigratedStatusFixture();
        fixture.Publish(fixture.Mock.CreateSnapshot(3));
        var attempts = fixture.Parameters.Attempts;
        fixture.ModbusSignals.OnNext(fixture.Mock.CreateSnapshot(1));

        fixture.SwitchToModbus();
        Dispatcher.UIThread.RunJobs();

        fixture.AssertStatus(1, "Ожидание", "#99445566", EquipmentStatusCategory.Information,
            EquipmentStatusQuality.Known, true, true);
        Assert.Equal(attempts, fixture.Parameters.Attempts);
        Assert.Empty(fixture.Commands.Requests);
    }

    [AvaloniaFact]
    public void Applying_status_text_keeps_stop_pending_until_live_readback()
    {
        using var fixture = new MigratedStatusFixture();
        var snapshot = fixture.Mock.CreateSnapshot(3).ToDictionary();
        const string stopId = "equip.bucket.stop";
        SetSignal(snapshot, stopId, false);
        fixture.Publish(snapshot);
        fixture.Card.RequestStopChange(true);
        Assert.True(fixture.Card.IsStopPending);

        var draft = fixture.Manager.CreateDraft();
        draft.Cards.Single().StatusMapping!.Entries.Single(entry => entry.Code == 3).Text = "Новый статус";
        Assert.True(fixture.Manager.Apply(draft).IsSuccess);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Новый статус", fixture.Card.StatusText);
        Assert.True(fixture.Card.IsStopPending);
        Assert.False(fixture.Card.CanStop);
        fixture.Card.RequestStopChange(true);
        Assert.Equal(stopId, Assert.Single(fixture.Commands.Requests).SignalId);

        SetSignal(snapshot, stopId, true);
        fixture.Signals.OnNext(snapshot);
        Dispatcher.UIThread.RunJobs();
        Assert.True(fixture.Card.IsStopChecked);
        Assert.False(fixture.Card.IsStopPending);
        Assert.Single(fixture.Commands.Requests);
    }

    [AvaloniaFact]
    public void Migrated_palette_with_historical_trailing_newline_renders_unknown_status()
    {
        using var fixture = new MigratedStatusFixture();
        var draft = fixture.Manager.CreateDraft();
        draft.Map.Palette.Warning = "#1122AB\n";
        Assert.True(fixture.Manager.Apply(draft).IsSuccess);
        var snapshot = fixture.Mock.CreateSnapshot(3).ToDictionary();
        SetText(snapshot, 9);
        fixture.Publish(snapshot);

        Assert.Equal("Неизвестный статус (код 9)", fixture.Card.StatusText);
        Assert.Equal(Color.Parse("#1122AB"), Assert.IsAssignableFrom<ISolidColorBrush>(fixture.Card.StatusBrush).Color);
        Assert.Equal("#1122AB\n", fixture.Manager.CurrentDocument.Map.Palette.Warning);
    }

    private static SignalValue TextSignal(object? value, SignalValueType type = SignalValueType.UInt16,
        bool isGood = true, bool isStale = false) =>
        new(MigratedStatusFixture.TextId, value, type, DateTimeOffset.UnixEpoch, isGood, isStale);

    private static void SetText(Dictionary<string, SignalValue> snapshot, ushort code) =>
        snapshot[MigratedStatusFixture.TextId] = TextSignal(code);

    private static void SetSignal(Dictionary<string, SignalValue> snapshot, string id, bool value,
        bool isGood = true, bool isStale = false) =>
        snapshot[id] = new(id, value, SignalValueType.Bool, DateTimeOffset.UnixEpoch, isGood, isStale);

    private sealed class MigratedStatusFixture : IDisposable
    {
        internal const string BucketId = "equip.bucket";
        internal const string TextId = "equip.bucket.text";
        internal const string PowerId = "equip.bucket.power";
        internal const string FaultId = "test.bucket.fault";
        internal const string EnabledId = "test.bucket.enabled";
        internal const string StartOffId = "test.bucket.start-off";
        internal const string StopOffId = "test.bucket.stop-off";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "route-map-status-tests", Guid.NewGuid().ToString("N"));
        private readonly RouteMapConfigurationMapper _mapper = new(RouteMapSeed.Create());
        private readonly RouteMapSignalRuntime _signalRuntime;
        private readonly RouteMapDashboardView _view;
        private readonly Window _window;
        private bool _dashboardDisposed;

        public MigratedStatusFixture()
        {
            Storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var historical = _mapper.CreateSeedDocument();
            historical.SchemaVersion = 17;
            var bucket = historical.Cards.Single();
            bucket.StatusMapping = null;
            bucket.Bindings.Add(BoolBinding(SignalBindingRole.Enabled, EnabledId));
            bucket.Bindings.Add(BoolBinding(SignalBindingRole.Fault, FaultId));
            bucket.Bindings.Add(BoolBinding(SignalBindingRole.StartOffFeedback, StartOffId));
            bucket.Bindings.Add(BoolBinding(SignalBindingRole.StopOffFeedback, StopOffId));
            historical.Map.Palette.MutedText = "#112233";
            historical.Map.Palette.Warning = "#99445566";
            historical.Map.Palette.Fault = "#778899";
            historical.Map.Palette.Ready = "#126534";
            Storage.SaveActive(historical);
            Manager = OpenManager();
            Assert.Null(Manager.LastLoadError);
            Mock = new MockSignalProvider(Manager, new MockSignalState());
            var journal = new RouteMapSessionJournal();
            var notifications = new NotificationsPanelViewModel(journal, new NoOpDialogService(), new NoOpBitWriter());
            var provider = new TestSignalProvider(Observable.Defer(() =>
            {
                Subscriptions++;
                return Signals;
            }));
            _signalRuntime = new RouteMapSignalRuntime(provider, Commands, new TestSignalProvider(ModbusSignals),
                Commands, RouteMapSignalSource.Mock);
            Dashboard = new RouteMapDashboardViewModel(Manager, _signalRuntime, new RouteMapRuntimeMapper(Manager),
                _signalRuntime, new NoOpSettingsDialogService(), notifications, journal,
                new StaticOptionsMonitor(new ModbusOptions()),
                Options.Create(new ApplicationOptions { WorkMode = ApplicationOptions.UserWorkMode }),
                parameterAutoDispatcher: Parameters);
            _view = new RouteMapDashboardView { DataContext = Dashboard };
            _window = new Window { Width = 1300, Height = 760, Content = _view };
            _window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public RouteMapConfigurationStorage Storage { get; }
        public RouteMapConfigurationManager Manager { get; }
        public MockSignalProvider Mock { get; }
        public RouteMapDashboardViewModel Dashboard { get; }
        public NoOpCommandDispatcher Commands { get; } = new();
        public RecordingParameterDispatcher Parameters { get; } = new();
        public Subject<IReadOnlyDictionary<string, SignalValue>> Signals { get; } = new();
        public ReplaySubject<IReadOnlyDictionary<string, SignalValue>> ModbusSignals { get; } = new(1);
        public int Subscriptions { get; private set; }
        public EquipmentCardViewModel Card => Dashboard.MapEquipmentCards.Single(card => card.Id == BucketId);
        public EquipmentCardView CardView => _view.GetVisualDescendants().OfType<EquipmentCardView>()
            .Single(view => ReferenceEquals(view.DataContext, Card));
        public RouteObjectRuntimeState Runtime => Dashboard.RuntimeState.Find(BucketId)!;
        public RouteMapPaletteSettings Palette => Manager.CurrentDefinition.Display!.Palette;
        public Color PowerColor =>
            Assert.IsAssignableFrom<ISolidColorBrush>(CardView.FindControl<Border>("PowerIndicator")!.Background).Color;

        public RouteMapConfigurationManager OpenManager() =>
            new(Storage, _mapper, new RouteMapConfigurationValidator(), new RouteMapConfigurationMigrator());

        public void SwitchToModbus() => _signalRuntime.SwitchSource(RouteMapSignalSource.Modbus);

        public void Publish(IReadOnlyDictionary<string, SignalValue> snapshot)
        {
            Signals.OnNext(snapshot.ToDictionary());
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(Commands.Requests);
        }

        public void AssertStatus(ushort? code, string text, string color, EquipmentStatusCategory? category,
            EquipmentStatusQuality quality, bool canStart, bool canStop)
        {
            var status = Assert.IsType<EquipmentStatusPresentation>(Runtime.EquipmentStatus);
            Assert.Equal((code, text, color, category, quality), (status.Code, status.Text, status.Color, status.Category, status.Quality));
            Assert.Equal((canStart, canStop), (Runtime.CanStart, Runtime.CanStop));
            Assert.Equal(status, Card.StatusPresentation);
            Assert.Equal((canStart, canStop), (Card.CanStart, Card.CanStop));
            Assert.Equal(Runtime.State, Card.State);
            Assert.Equal(text, Card.StatusText);
            var label = Assert.Single(CardView.GetVisualDescendants().OfType<TextBlock>(), item => item.Text == text);
            Assert.Equal(Color.Parse(color), Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
            Assert.Equal(status.DiagnosticText, ToolTip.GetTip(label));
            Assert.Equal(canStart, CardView.FindControl<Button>("StartButton")!.IsEffectivelyEnabled);
            Assert.Equal(canStop, CardView.FindControl<ToggleButton>("StopButton")!.IsEffectivelyEnabled);
        }

        public void Dispose()
        {
            _window.Close();
            DisposeDashboard();
            _signalRuntime.Dispose();
            Assert.False(Signals.HasObservers);
            Signals.Dispose();
            ModbusSignals.Dispose();
            Manager.Dispose();
            Directory.Delete(_directory, recursive: true);
        }

        public void DisposeDashboard()
        {
            if (_dashboardDisposed)
                return;

            Dashboard.Dispose();
            _dashboardDisposed = true;
        }

        private static SignalBindingConfiguration BoolBinding(SignalBindingRole role, string id) =>
            new() { Role = role, SignalId = id, Direction = SignalBindingDirection.Read, ValueType = SignalValueType.Bool };
    }

    private sealed class RecordingParameterDispatcher : IEquipmentParameterAutoDispatcher
    {
        public int Attempts { get; private set; }

        public Task DispatchPendingAsync(CancellationToken cancellationToken = default)
        {
            Attempts++;
            return Task.CompletedTask;
        }
    }

    private sealed class StatusFilePicker(string importPath, string exportPath) : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(importPath);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(exportPath);
    }
}
