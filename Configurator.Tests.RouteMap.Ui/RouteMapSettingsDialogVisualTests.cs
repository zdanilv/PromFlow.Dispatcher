using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogHostAvalonia;
using Configurator.Application.Services;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Data;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Modbus.Validation;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Signals;
using Configurator.Application.Services.OpcUa.Browsing;
using Configurator.Application.Services.OpcUa.Tags;
using Configurator.Desktop.Dialogs.AlarmNotificationDialog;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.EquipmentCardParametersDialog;
using Configurator.Desktop.Dialogs.HelpDialog;
using Configurator.Desktop.Dialogs.ModbusSettingsDialog;
using Configurator.Desktop.Main;
using Configurator.Desktop.Workspace.Alarms;
using Configurator.Desktop.Workspace.RouteMap;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Controls;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Panels;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Configurator.Desktop.Workspace;
using Material.Icons.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class RouteMapSettingsDialogVisualTests
{
    [AvaloniaFact]
    public void RouteMap_dashboard_shows_admin_legend_and_selected_object_prefix()
    {
        var directory = Path.Combine(Path.GetTempPath(), "route-map-legend-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var manager = new RouteMapConfigurationManager(
                new RouteMapConfigurationStorage(Path.Combine(directory, "route-map.json")),
                new RouteMapConfigurationMapper(RouteMapSeed.Create()),
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            using var adminNotifications = new NotificationsPanelViewModel(
                new RouteMapSessionJournal(),
                new NoOpDialogService(),
                new NoOpBitWriter());
            using var adminViewModel = CreateRouteMapDashboardViewModel(
                manager,
                adminNotifications,
                ApplicationOptions.AdminWorkMode);
            var adminView = new RouteMapDashboardView { DataContext = adminViewModel };
            var adminWindow = new Window { Width = 1300, Height = 760, Content = adminView };
            adminWindow.Show();
            Dispatcher.UIThread.RunJobs();

            var connectionStatus = adminView.FindControl<Border>("ConnectionStatusOverlay")!;
            var legend = adminView.FindControl<Border>("RouteMapStateLegendOverlay")!;
            Assert.True(adminViewModel.MapEquipmentCards.Single().IsParametersButtonEnabled);
            var selectedObjectText = adminView.FindControl<TextBlock>("SelectedObjectText")!;
            var expectedLabels = new[]
            {
                "Обозначения",
                "Выбранный узел",
                "Наведённый узел",
                "Активный узел",
                "Узел — точка отправки",
                "Узел — точка возврата",
                "Авария узла",
                "Узел не в сети",
                "Активная линия",
                "Авария линии",
                "Линия не в сети"
            };
            var expectedGlyphKinds = new[]
            {
                RouteMapLegendGlyphKind.SelectedNode,
                RouteMapLegendGlyphKind.HoveredNode,
                RouteMapLegendGlyphKind.ActiveNode,
                RouteMapLegendGlyphKind.TargetNode,
                RouteMapLegendGlyphKind.LoaderNode,
                RouteMapLegendGlyphKind.FaultNode,
                RouteMapLegendGlyphKind.OfflineNode,
                RouteMapLegendGlyphKind.ActiveSegment,
                RouteMapLegendGlyphKind.FaultSegment,
                RouteMapLegendGlyphKind.OfflineSegment
            };

            Assert.True(connectionStatus.IsVisible);
            Assert.True(legend.IsVisible);
            Assert.True(legend.Bounds.Top >= connectionStatus.Bounds.Bottom);
            Assert.Equal(
                expectedLabels,
                legend.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray());
            var glyphs = legend.GetVisualDescendants().OfType<RouteMapLegendGlyph>().ToArray();
            Assert.Equal(expectedGlyphKinds, glyphs.Select(glyph => glyph.Kind).ToArray());
            Assert.All(glyphs, glyph => Assert.Same(adminViewModel.Definition, glyph.Definition));
            Assert.All(glyphs, glyph =>
            {
                Assert.Equal(40, glyph.Bounds.Width);
                Assert.Equal(30, glyph.Bounds.Height);
            });
            Assert.Equal("Выбран - Объект не выбран", TextOf(selectedObjectText));

            adminViewModel.SelectedObjectId = "bsu_1";
            Dispatcher.UIThread.RunJobs();
            var selectedTitle = adminViewModel.Definition.Nodes.Single(node => node.Id == "bsu_1").Title;
            Assert.Equal($"Выбран - {selectedTitle}", TextOf(selectedObjectText));
            adminWindow.Close();

            using var userNotifications = new NotificationsPanelViewModel(
                new RouteMapSessionJournal(),
                new NoOpDialogService(),
                new NoOpBitWriter());
            using var userViewModel = CreateRouteMapDashboardViewModel(
                manager,
                userNotifications,
                ApplicationOptions.UserWorkMode);
            var userView = new RouteMapDashboardView { DataContext = userViewModel };
            var userWindow = new Window { Width = 1300, Height = 760, Content = userView };
            userWindow.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.False(userView.FindControl<Border>("ConnectionStatusOverlay")!.IsVisible);
            Assert.True(userView.FindControl<Border>("RouteMapStateLegendOverlay")!.IsVisible);
            Assert.True(userViewModel.MapEquipmentCards.Single().IsParametersButtonEnabled);
            userWindow.Close();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Alarm_notification_dialog_uses_dedicated_unframed_host()
    {
        var originalServices = Configurator.Desktop.App.Services;
        using var serviceProvider = new ServiceCollection()
            .AddSingleton<IAppConfigService>(new NullAppConfigService())
            .AddTransient<AlarmNotificationDialogView>()
            .BuildServiceProvider();
        Configurator.Desktop.App.Services = serviceProvider;
        var dialogHostStyles = new DialogHostStyles();
        Avalonia.Application.Current!.Styles.Add(dialogHostStyles);

        try
        {
            var window = new MainWindow { Width = 800, Height = 600 };
            var rootHost = Assert.IsType<DialogHost>(window.Content);
            var alarmHost = Assert.IsType<DialogHost>(rootHost.Content);
            var factory = new DialogViewFactory(serviceProvider);
            var context = factory.CreateAlarmNotification(ModbusAlarmKind.Fault, "Авария");

            Assert.Equal(DialogHostIds.Root, rootHost.Identifier);
            Assert.Equal(DialogHostIds.AlarmNotification, alarmHost.Identifier);
            Assert.Equal(new Thickness(-1), alarmHost.DialogMargin);
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(alarmHost.Background).Color);
            Assert.Equal(
                new Thickness(0),
                alarmHost.GetValue(DialogHostStyle.BorderThicknessProperty));
            Assert.Equal(
                Colors.Transparent,
                Assert.IsAssignableFrom<ISolidColorBrush>(
                    alarmHost.GetValue(DialogHostStyle.BorderBrushProperty)).Color);
            Assert.Equal(
                new CornerRadius(5),
                alarmHost.GetValue(DialogHostStyle.CornerRadiusProperty));
            Assert.Equal(
                "none",
                alarmHost.GetValue(DialogHostStyle.BoxShadowProperty).ToString());
            Assert.True(alarmHost.GetValue(DialogHostStyle.ClipToBoundsProperty));
            Assert.Equal(DialogHostIds.AlarmNotification, context.HostIdentifier);

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var showTask = DialogHost.Show(context.View, context.HostIdentifier);
            Dispatcher.UIThread.RunJobs();

            Assert.True(context.View.IsVisible);
            Assert.Equal(new Point(-1, -1), context.View.Bounds.Position);

            DialogHost.Close(context.HostIdentifier, false);
            Dispatcher.UIThread.RunJobs();
            showTask.GetAwaiter().GetResult();
            window.Close();
        }
        finally
        {
            Avalonia.Application.Current?.Styles.Remove(dialogHostStyles);
            Configurator.Desktop.App.Services = originalServices;
        }
    }

    [AvaloniaTheory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Binding_rows_have_five_non_overlapping_columns(int tabIndex)
    {
        using var fixture = new DialogFixture(1320, 780);
        fixture.SelectTab(tabIndex);

        var rows = fixture.Dialog.GetVisualDescendants()
            .OfType<Grid>()
            .Where(x => x.Classes.Contains("signal-binding-row"))
            .ToArray();

        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            var controls = row.Children.OfType<Control>().ToArray();
            Assert.Equal(5, controls.Length);
            Assert.Equal([0, 1, 2, 3, 4], controls.Select(Grid.GetColumn).ToArray());
            Assert.All(controls, control =>
            {
                Assert.True(control.IsVisible);
                Assert.True(control.Bounds.Width > 0);
                Assert.True(control.Bounds.Height > 0);
            });

            for (var index = 1; index < controls.Length; index++)
                Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);
        }
    }

    [AvaloniaTheory]
    [InlineData(3, "Положение подписи")]
    [InlineData(3, "Цвет сигнального контура")]
    [InlineData(3, "Толщина сигнального контура")]
    [InlineData(4, "Зазор от узлов")]
    [InlineData(4, "Края линии")]
    [InlineData(4, "Отрезки")]
    public void New_visual_properties_are_present_in_settings_tabs(int tabIndex, string label)
    {
        using var fixture = new DialogFixture(1320, 780);
        fixture.SelectTab(tabIndex);

        var labels = fixture.Dialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(x => x.Text)
            .ToArray();

        Assert.Contains(label, labels);
    }

    [AvaloniaTheory]
    [InlineData(2, "Обычный фон")]
    [InlineData(2, "Pressed фон")]
    [InlineData(2, "Активный фон")]
    [InlineData(2, "Pressed текст")]
    [InlineData(5, "ПУСК фон")]
    [InlineData(5, "СТОП checked текст")]
    public void Button_color_properties_are_not_present_in_settings_tabs(int tabIndex, string label)
    {
        using var fixture = new DialogFixture(1320, 780);
        fixture.SelectTab(tabIndex);

        var labels = fixture.Dialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(x => x.Text)
            .ToArray();

        Assert.DoesNotContain(label, labels);
    }

    [AvaloniaFact]
    public void Narrow_binding_editor_uses_horizontal_scrolling_without_overlap()
    {
        var editor = new SignalBindingsEditor
        {
            Width = 620,
            Bindings = new[]
            {
                new SignalBindingConfiguration
                {
                    Role = SignalBindingRole.Visible,
                    SignalId = "node.visible",
                    Direction = SignalBindingDirection.Read,
                    ValueType = Configurator.Application.Services.Signals.SignalValueType.Bool,
                },
            },
        };
        var window = new Window { Width = 660, Height = 260, Content = editor };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var scroll = editor.GetVisualDescendants()
            .OfType<ScrollViewer>()
            .Single(x => x.Classes.Contains("signal-bindings-scroll"));
        var row = editor.GetVisualDescendants()
            .OfType<Grid>()
            .Single(x => x.Classes.Contains("signal-binding-row"));
        var controls = row.Children.OfType<Control>().ToArray();

        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        for (var index = 1; index < controls.Length; index++)
            Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);

        window.Close();
    }

    [AvaloniaFact]
    public void All_settings_tabs_complete_layout_without_exceptions()
    {
        using var fixture = new DialogFixture(1320, 780);

        for (var tabIndex = 0; tabIndex < 7; tabIndex++)
        {
            fixture.SelectTab(tabIndex);
            fixture.Dialog.InvalidateMeasure();
            fixture.Dialog.InvalidateVisual();
            Dispatcher.UIThread.RunJobs();
            Assert.True(fixture.Dialog.Bounds.Width > 0);
            Assert.True(fixture.Dialog.Bounds.Height > 0);
        }
    }

    [AvaloniaFact]
    public void Workspace_places_signal_mapping_tab_immediately_after_route_map()
    {
        var view = new WorkspaceView { DataContext = CreateWorkspaceViewModel(isAdminMode: true) };
        var window = new Window { Width = 1200, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var root = Assert.IsType<Grid>(view.Content);
        var tabs = root.Children.OfType<TabControl>().Single();
        var headers = tabs.Items.Cast<TabItem>().Select(item => item.Header?.ToString() ?? string.Empty).ToArray();

        Assert.True(tabs.IsVisible);
        Assert.Equal(["Route Map", "SignalId ↔ Modbus", "Менеджер тревог", "Modbus TCP"], headers);

        window.Close();
    }

    [AvaloniaFact]
    public void Workspace_user_mode_shows_route_map_without_tabs()
    {
        var view = new WorkspaceView { DataContext = CreateWorkspaceViewModel(isAdminMode: false) };
        var window = new Window { Width = 1200, Height = 760, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var root = Assert.IsType<Grid>(view.Content);
        var tabs = root.Children.OfType<TabControl>().Single();
        var routeMapHost = view.FindControl<Grid>("UserRouteMapHost")!;

        Assert.False(tabs.IsVisible);
        Assert.True(routeMapHost.IsVisible);
        Assert.Single(routeMapHost.Children.OfType<RouteMapDashboardView>());

        window.Close();
    }

    [AvaloniaFact]
    public void Alarm_manager_rows_scroll_horizontally_without_overlap()
    {
        var options = new ModbusOptions
        {
            Client = new ModbusEndpointOptions
            {
                CoilCount = 8,
                RegisterCount = 8,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true
            },
            Server = new ModbusEndpointOptions
            {
                CoilCount = 8,
                RegisterCount = 8,
                CoilsEnabled = true,
                HoldingRegistersEnabled = true
            },
            AlarmMap =
            [
                new()
                {
                    Id = "alarm.main",
                    Kind = ModbusAlarmKind.Fault,
                    Message = "Авария",
                    Alarm = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.HoldingRegister,
                        Address = 1,
                        BitIndex = 0
                    },
                    Acknowledgement = new ModbusBitAddressOptions
                    {
                        Area = ModbusDataArea.HoldingRegister,
                        Address = 1,
                        BitIndex = 1
                    }
                }
            ]
        };
        using var viewModel = new AlarmManagerViewModel(
            new StaticOptionsMonitor(options),
            new NullAppConfigService(),
            new ModbusAlarmMapValidator());
        var view = new AlarmManagerView { Width = 900, DataContext = viewModel };
        var window = new Window { Width = 940, Height = 360, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var row = view.GetVisualDescendants()
            .OfType<Grid>()
            .Single(x => x.Classes.Contains("alarm-manager-row"));
        var controls = row.Children.OfType<Control>().ToArray();
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();

        Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
        Assert.Equal(23, controls.Length);
        Assert.Equal(Enumerable.Range(0, 23), controls.Select(Grid.GetColumn));
        for (var index = 1; index < controls.Length; index++)
            Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);

        window.Close();
    }

    [AvaloniaFact]
    public void Alarm_dialog_kinds_have_distinct_visual_style_and_buttons()
    {
        var fault = new AlarmNotificationDialogViewModel(
            ModbusAlarmKind.Fault,
            "Основное сообщение",
            "Текущее значение: 42");
        var confirmation = new AlarmNotificationDialogViewModel(ModbusAlarmKind.Confirmation, "Повторное подтверждение");

        var message = new AlarmNotificationDialogViewModel(ModbusAlarmKind.Message, "Сообщение");

        Assert.NotEqual(
            ((SolidColorBrush)fault.HeaderBackground).Color,
            ((SolidColorBrush)confirmation.HeaderBackground).Color);
        Assert.NotEqual(
            ((SolidColorBrush)fault.HeaderBackground).Color,
            ((SolidColorBrush)message.HeaderBackground).Color);
        Assert.NotEqual(
            ((SolidColorBrush)confirmation.HeaderBackground).Color,
            ((SolidColorBrush)message.HeaderBackground).Color);
        Assert.Equal("Сообщение", message.Title);

        var view = new AlarmNotificationDialogView { DataContext = fault };
        var window = new Window { Width = 540, Height = 320, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .ToArray();
        var buttons = view.GetVisualDescendants().OfType<Button>().ToArray();
        var dialogFrame = view.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("alarm-notification-dialog-frame"));
        var dialogContent = view.GetVisualDescendants()
            .OfType<Border>()
            .Single(border => border.Classes.Contains("alarm-notification-dialog-content"));
        var closeButton = buttons.Single(button => button.Classes.Contains("alarm-notification-dialog-close"));
        var closeIcon = closeButton.GetVisualDescendants().OfType<MaterialIcon>().Single();

        Assert.Contains("Авария", texts);
        Assert.Contains("Основное сообщение", texts);
        Assert.Contains("Текущее значение: 42", texts);
        Assert.Contains(buttons, button => button.Content?.ToString() == "Хорошо");
        Assert.DoesNotContain(buttons, button => button.Content?.ToString() == "X");
        Assert.Equal("Multiply", closeIcon.Kind.ToString());
        Assert.Equal(18, closeIcon.FontSize);
        Assert.Equal(
            Assert.IsAssignableFrom<ISolidColorBrush>(fault.HeaderBackground).Color,
            Assert.IsAssignableFrom<ISolidColorBrush>(dialogFrame.Background).Color);
        Assert.Equal(Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(dialogContent.Background).Color);
        var mainMessage = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Text == "Основное сообщение");
        var registerValue = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Single(text => text.Text == "Текущее значение: 42");
        Assert.True(registerValue.IsVisible);
        Assert.True(registerValue.Bounds.Top >= mainMessage.Bounds.Bottom);

        window.Close();
    }

    [AvaloniaFact]
    public void Notifications_panel_wraps_long_alarm_message_without_expanding_panel_width()
    {
        var directory = Path.Combine(Path.GetTempPath(), "route-map-long-notification-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var manager = new RouteMapConfigurationManager(
                new RouteMapConfigurationStorage(Path.Combine(directory, "route-map.json")),
                new RouteMapConfigurationMapper(RouteMapSeed.Create()),
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            var baselineJournal = new RouteMapSessionJournal();
            baselineJournal.ShowAlarmNotification(
                new ModbusAlarmOptions
                {
                    Id = "alarm.short-message",
                    Kind = ModbusAlarmKind.Fault,
                    Message = "Короткое сообщение",
                },
                DateTimeOffset.UtcNow,
                markUnread: true);
            var baselinePanelWidth = MeasureNotificationsPanelWidth(baselineJournal);

            var journal = new RouteMapSessionJournal();
            journal.ShowAlarmNotification(
                new ModbusAlarmOptions
                {
                    Id = "alarm.long-message",
                    Kind = ModbusAlarmKind.Fault,
                    Message = new string('А', 800),
                },
                DateTimeOffset.UtcNow,
                markUnread: true,
                registerValueText: "Значение: 42");
            using var notificationsPanel = new NotificationsPanelViewModel(
                journal,
                new NoOpDialogService(),
                new NoOpBitWriter());
            using var viewModel = new RouteMapDashboardViewModel(
                manager,
                new EmptySignalProvider(),
                new RouteMapRuntimeMapper(manager.CurrentDefinition),
                new NoOpCommandDispatcher(),
                new NoOpSettingsDialogService(),
                notificationsPanel,
                journal,
                new StaticOptionsMonitor(new ModbusOptions()));
            var view = new RouteMapDashboardView { DataContext = viewModel };
            var window = new Window { Width = 1300, Height = 760, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var panel = view.GetVisualDescendants().OfType<NotificationsPanelView>().Single();
            var card = panel.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("alarm-notification"));
            var header = panel.GetVisualDescendants()
                .OfType<Border>()
                .Single(border => border.Classes.Contains("alarm-notification-header"));
            var dismissButton = panel.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Classes.Contains("alarm-notification-dismiss"));
            var createdAt = panel.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Classes.Contains("alarm-notification-created-at"));
            var message = panel.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Classes.Contains("alarm-notification-message"));
            var registerValue = panel.GetVisualDescendants()
                .OfType<TextBlock>()
                .Single(text => text.Classes.Contains("alarm-notification-register-value"));
            var messageContent = panel.GetVisualDescendants()
                .OfType<Grid>()
                .Single(grid => grid.Classes.Contains("alarm-notification-message-content"));
            var closeIcon = dismissButton.GetVisualDescendants().OfType<MaterialIcon>().Single();

            Assert.Equal(baselinePanelWidth, panel.Bounds.Width);
            Assert.InRange(card.Bounds.Width, 0, 376.01);
            Assert.True(dismissButton.Bounds.Left >= createdAt.Bounds.Right);
            Assert.True(dismissButton.Bounds.Top >= header.Bounds.Top);
            Assert.True(dismissButton.Bounds.Bottom <= header.Bounds.Bottom);
            // Material Icons maps the Close alias to the Multiply glyph in the resolved control.
            Assert.Equal("Multiply", closeIcon.Kind.ToString());
            Assert.Equal(14, closeIcon.FontSize);
            Assert.Equal(TextWrapping.WrapWithOverflow, message.TextWrapping);
            Assert.Equal(VerticalAlignment.Center, message.VerticalAlignment);
            Assert.True(registerValue.IsVisible);
            Assert.True(registerValue.Bounds.Top >= message.Bounds.Bottom);
            Assert.True(message.Bounds.Height > 74);
            Assert.True(card.Bounds.Height > 120);
            Assert.True(messageContent.Bounds.Height >= message.Bounds.Height);

            window.Close();

            double MeasureNotificationsPanelWidth(RouteMapSessionJournal sourceJournal)
            {
                using var sourceNotificationsPanel = new NotificationsPanelViewModel(
                    sourceJournal,
                    new NoOpDialogService(),
                    new NoOpBitWriter());
                using var sourceViewModel = new RouteMapDashboardViewModel(
                    manager,
                    new EmptySignalProvider(),
                    new RouteMapRuntimeMapper(manager.CurrentDefinition),
                    new NoOpCommandDispatcher(),
                    new NoOpSettingsDialogService(),
                    sourceNotificationsPanel,
                    sourceJournal,
                    new StaticOptionsMonitor(new ModbusOptions()));
                var sourceView = new RouteMapDashboardView { DataContext = sourceViewModel };
                var sourceWindow = new Window { Width = 1300, Height = 760, Content = sourceView };
                sourceWindow.Show();
                Dispatcher.UIThread.RunJobs();

                var width = sourceView.GetVisualDescendants()
                    .OfType<NotificationsPanelView>()
                    .Single()
                    .Bounds.Width;
                sourceWindow.Close();
                return width;
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Notifications_panel_has_clear_button_dynamic_history_width_and_history_columns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "route-map-notifications-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var journal = new RouteMapSessionJournal();
        var activeAlarm = new ModbusAlarmOptions
        {
            Id = "alarm.main",
            Kind = ModbusAlarmKind.Fault,
            Message = "Авария привода",
            Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 0 },
            Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 1 }
        };
        var closedAlarm = new ModbusAlarmOptions
        {
            Id = "alarm.closed",
            Kind = ModbusAlarmKind.Message,
            Message = "Закрытое сообщение",
            Alarm = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 2 },
            Acknowledgement = new ModbusBitAddressOptions { Area = ModbusDataArea.Coil, Address = 3 }
        };
        journal.ShowAlarmNotification(
            activeAlarm,
            DateTimeOffset.UtcNow,
            markUnread: true);
        journal.ShowAlarmNotification(
            closedAlarm,
            DateTimeOffset.UtcNow,
            markUnread: true);
        journal.RecordAlarmCleared(closedAlarm, DateTimeOffset.UtcNow);
        journal.RecordSignalSent(
            new SignalWriteRequest("equip.bucket.start", true, SignalValueType.Bool),
            RouteMapSeed.Create(),
            new ModbusOptions
            {
                DataMap =
                [
                    new()
                    {
                        Name = "equip.bucket.start",
                        Area = ModbusDataArea.Coil,
                        Address = 3,
                        Type = ModbusValueType.Bool,
                        Access = ModbusDataAccess.ReadWrite
                    }
                ]
            },
            DateTimeOffset.UtcNow);

        try
        {
            using var manager = new RouteMapConfigurationManager(
                new RouteMapConfigurationStorage(Path.Combine(directory, "route-map.json")),
                new RouteMapConfigurationMapper(RouteMapSeed.Create()),
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            using var notificationsPanel = new NotificationsPanelViewModel(
                journal,
                new NoOpDialogService(),
                new NoOpBitWriter());
            using var viewModel = new RouteMapDashboardViewModel(
                manager,
                new EmptySignalProvider(),
                new RouteMapRuntimeMapper(manager.CurrentDefinition),
                new NoOpCommandDispatcher(),
                new NoOpSettingsDialogService(),
                notificationsPanel,
                journal,
                new StaticOptionsMonitor(new ModbusOptions()));
            var view = new RouteMapDashboardView { DataContext = viewModel };
            var window = new Window { Width = 1300, Height = 760, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            var panel = view.GetVisualDescendants().OfType<NotificationsPanelView>().Single();
            var connectionStatus = view.FindControl<Border>("ConnectionStatusOverlay")!;
            var tabs = panel.GetVisualDescendants().OfType<TabControl>().Single();
            var headers = tabs.Items.Cast<TabItem>().Select(item => item.Header?.ToString() ?? string.Empty).ToArray();

            Assert.True(panel.IsVisible);
            Assert.True(connectionStatus.IsVisible);
            var notificationsWidth = panel.Bounds.Width;
            Assert.True(notificationsWidth >= 400);
            Assert.Equal(["Уведомления", "История"], headers);
            Assert.Contains("Авария привода", panel.GetVisualDescendants().OfType<TextBlock>().Select(item => item.Text));
            var clearButton = panel.GetVisualDescendants()
                .OfType<Button>()
                .Single(button => button.Content?.ToString() == "Очистить список");
            Assert.True(clearButton.IsEnabled);

            clearButton.Command!.Execute(clearButton.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            var notification = Assert.Single(journal.Notifications);
            Assert.Equal("alarm.main", notification.Id);

            tabs.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.True(panel.Bounds.Width > notificationsWidth);
            Assert.True(panel.Bounds.Width > 400);
            var textValues = panel.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(item => item.Text)
                .ToArray();
            Assert.Contains("↑↓", textValues);
            Assert.Contains("Роли / объекты", textValues);
            Assert.Contains("↑", textValues);
            Assert.Contains("3", textValues);
            Assert.DoesNotContain("Coil 3", textValues);
            var rows = panel.GetVisualDescendants()
                .OfType<Grid>()
                .Where(grid => grid.Classes.Contains("route-map-history-row"))
                .ToArray();
            Assert.NotEmpty(rows);
            foreach (var row in rows)
            {
                var controls = row.Children.OfType<Control>().ToArray();
                Assert.Equal(7, controls.Length);
                Assert.Equal(Enumerable.Range(0, 7), controls.Select(Grid.GetColumn));
                for (var index = 1; index < controls.Length; index++)
                    Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);
            }

            window.Close();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaFact]
    public void Notifications_panel_hides_history_tab_in_user_mode()
    {
        var journal = new RouteMapSessionJournal();
        using var viewModel = new NotificationsPanelViewModel(
            journal,
            new NoOpDialogService(),
            new NoOpBitWriter(),
            Options.Create(new ApplicationOptions { WorkMode = ApplicationOptions.UserWorkMode }));
        var view = new NotificationsPanelView { DataContext = viewModel };
        var window = new Window { Width = 500, Height = 500, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        var history = tabs.Items.Cast<TabItem>().Single(item => item.Header?.ToString() == "История");

        Assert.False(viewModel.IsHistoryVisible);
        Assert.False(history.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void Equipment_card_has_parameters_button_and_dialog_layout()
    {
        var card = RouteMapSeed.Create().MapEquipment.Single() with
        {
            Parameters =
            [
                new EquipmentCardParameter(
                    "Скорость",
                    new SignalBinding(
                        SignalBindingRole.EquipmentParameter,
                        "equip.bucket.speed",
                        SignalBindingDirection.ReadWrite,
                        SignalValueType.Word)),
                new EquipmentCardParameter(
                    "Разрешение",
                    new SignalBinding(
                        SignalBindingRole.EquipmentParameter,
                        "equip.bucket.enabled",
                        SignalBindingDirection.ReadWrite,
                        SignalValueType.Bool))
            ]
        };
        var dispatcher = new NoOpCommandDispatcher();
        var cardViewModel = new EquipmentCardViewModel(card, dispatcher);
        var cardView = new EquipmentCardView { DataContext = cardViewModel };
        var cardWindow = new Window { Width = 340, Height = 200, Content = cardView };
        cardWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var parameterButton = cardView.FindControl<Button>("ParametersButton")!;
        var selectorButton = cardView.FindControl<ToggleButton>("SelectorButton")!;
        var startButton = cardView.FindControl<ToggleButton>("StartButton")!;
        var stopButton = cardView.FindControl<ToggleButton>("StopButton")!;
        Assert.InRange(parameterButton.Bounds.Width, 39, 41);
        Assert.InRange(parameterButton.Bounds.Height, 39, 41);
        Assert.InRange(selectorButton.Bounds.Width, 39, 41);
        Assert.InRange(selectorButton.Bounds.Height, 39, 41);
        Assert.True(selectorButton.Bounds.Right <= parameterButton.Bounds.Left);
        Assert.Equal(Color.Parse("#CCD3D8"), Assert.IsAssignableFrom<ISolidColorBrush>(selectorButton.BorderBrush).Color);
        Assert.Equal(new Thickness(1), selectorButton.BorderThickness);
        Assert.Equal(HorizontalAlignment.Center, selectorButton.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, selectorButton.VerticalContentAlignment);
        Assert.Equal(Color.Parse("#CCD3D8"), Assert.IsAssignableFrom<ISolidColorBrush>(startButton.BorderBrush).Color);
        Assert.Equal(new Thickness(1), startButton.BorderThickness);
        Assert.Equal(HorizontalAlignment.Center, startButton.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, startButton.VerticalContentAlignment);
        Assert.Equal(HorizontalAlignment.Center, stopButton.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, stopButton.VerticalContentAlignment);

        cardViewModel.IsStopHovered = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#FF8A8A"), Assert.IsType<SolidColorBrush>(stopButton.Background).Color);

        cardViewModel.IsStartHovered = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#8AFF8E"), Assert.IsType<SolidColorBrush>(startButton.Background).Color);
        cardViewModel.IsStartHovered = false;

        cardViewModel.IsSelectorChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#003CA3"), Assert.IsType<SolidColorBrush>(selectorButton.Background).Color);

        cardViewModel.IsSelectorPressed = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#8AB5FF"), Assert.IsType<SolidColorBrush>(selectorButton.Background).Color);
        cardViewModel.IsSelectorPressed = false;

        cardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Disabled,
            card.StatusText,
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsEnabled: false));
        Dispatcher.UIThread.RunJobs();
        Assert.True(selectorButton.IsEnabled);
        Assert.True(parameterButton.IsEffectivelyEnabled);
        Assert.Equal(Color.Parse("#D0D0D0"), Assert.IsType<SolidColorBrush>(parameterButton.Background).Color);
        Assert.Equal(Color.Parse("#D0D0D0"), Assert.IsType<SolidColorBrush>(selectorButton.Background).Color);

        var userCardViewModel = new EquipmentCardViewModel(card, dispatcher);
        var userCardView = new EquipmentCardView { DataContext = userCardViewModel };
        var userCardWindow = new Window { Width = 340, Height = 200, Content = userCardView };
        userCardWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var userParameterButton = userCardView.FindControl<Button>("ParametersButton")!;
        Assert.True(userParameterButton.IsEffectivelyEnabled);

        userCardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Disabled,
            card.StatusText,
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsEnabled: false), isConnectionAvailable: true);
        Dispatcher.UIThread.RunJobs();
        Assert.True(userParameterButton.IsEffectivelyEnabled);

        userCardViewModel.ApplyRuntime(new RouteObjectRuntimeState(
            card.Id,
            RouteObjectState.Offline,
            "Не в сети",
            ValueText: null,
            IsVisible: true,
            CanStart: false,
            CanStop: false,
            IsEnabled: false), isConnectionAvailable: false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(userParameterButton.IsEffectivelyEnabled);
        userCardWindow.Close();

        var dialogViewModel = new EquipmentCardParametersDialogViewModel(card, null, dispatcher);
        var dialog = new EquipmentCardParametersDialogView { DataContext = dialogViewModel };
        var dialogWindow = new Window { Width = 560, Height = 420, Content = dialog };
        dialogWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = dialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => text.Text)
            .ToArray();
        var buttons = dialog.GetVisualDescendants()
            .OfType<Button>()
            .Select(button => button.Content?.ToString())
            .ToArray();

        Assert.Contains("Настройки оборудования", texts);
        Assert.Contains("Скорость", texts);
        Assert.Contains("Разрешение", texts);
        Assert.Contains("equip.bucket.speed • Word", texts);
        Assert.Contains("equip.bucket.enabled • Bool", texts);
        Assert.Contains("Закрыть", buttons);
        Assert.Contains("Сохранить", buttons);
        Assert.Single(dialog.GetVisualDescendants().OfType<TextBox>(), textBox => textBox.IsVisible);
        Assert.Single(dialog.GetVisualDescendants().OfType<ToggleSwitch>(), toggle => toggle.IsVisible);

        dialogWindow.Close();

        var userDialogViewModel = new EquipmentCardParametersDialogViewModel(
            card,
            null,
            dispatcher,
            showTechnicalDetails: false);
        var userDialog = new EquipmentCardParametersDialogView { DataContext = userDialogViewModel };
        var userDialogWindow = new Window { Width = 560, Height = 420, Content = userDialog };
        userDialogWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var userTexts = userDialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => text.Text)
            .ToArray();
        Assert.Contains("Скорость", userTexts);
        Assert.Contains("Разрешение", userTexts);
        Assert.DoesNotContain("equip.bucket.speed • Word", userTexts);
        Assert.DoesNotContain("equip.bucket.enabled • Bool", userTexts);

        userDialogWindow.Close();
        cardWindow.Close();
    }

    [AvaloniaFact]
    public void Card_settings_add_parameter_and_signal_mapping_includes_it()
    {
        using var fixture = new DialogFixture(1320, 780);
        fixture.SelectTab(5);

        var textValues = fixture.Dialog.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(text => text.Text)
            .ToArray();
        Assert.Contains("Настройки оборудования", textValues);
        var addButton = fixture.Dialog.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Content?.ToString() == "Добавить настройку");

        addButton.Command!.Execute(addButton.CommandParameter);
        Dispatcher.UIThread.RunJobs();

        var parameter = Assert.Single(fixture.ViewModel.SelectedCard!.Parameters);
        Assert.Equal("Параметр", parameter.Title);
        Assert.Equal(SignalBindingRole.EquipmentParameter, parameter.Role);
        Assert.Equal($"{fixture.ViewModel.SelectedCard.Id}.parameter", parameter.SignalId);
        Assert.Equal(SignalBindingDirection.ReadWrite, parameter.Direction);
        Assert.Equal(SignalValueType.Word, parameter.ValueType);
        parameter.Title = "Скорость";
        parameter.SignalId = "equip.bucket.speed";
        fixture.ViewModel.Apply();

        using var mappingViewModel = new RouteMapSignalMappingViewModel(
            fixture.Manager,
            new StaticOptionsMonitor(new ModbusOptions()),
            new NullAppConfigService(),
            new ModbusDataMapValidator(),
            new NoOpDataMapRuntime());
        var mappingView = new RouteMapSignalMappingView { DataContext = mappingViewModel };
        var window = new Window { Width = 1200, Height = 760, Content = mappingView };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var row = SignalMappingRowGrid(mappingView, "equip.bucket.speed");
        var rowModel = Assert.IsType<RouteMapSignalMappingRow>(row.DataContext);
        Assert.Equal(SignalValueType.Word, rowModel.ExpectedType);
        Assert.Equal(ModbusDataAccess.ReadWrite, rowModel.RequiredAccess);
        Assert.Contains(nameof(SignalBindingRole.EquipmentParameter), rowModel.Roles);

        mappingViewModel.CreateMappingCommand.Execute(rowModel).Subscribe();
        Assert.Equal(ModbusValueType.Word, rowModel.Type);
        Assert.Equal(1, rowModel.Length);

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_does_not_show_admin_text_or_arrow()
    {
        var view = new TopBarView { DataContext = new TopBarViewModel() };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants()
            .OfType<TextBlock>()
            .Select(textBlock => textBlock.Text)
            .ToArray();

        Assert.DoesNotContain("Admin", texts);
        Assert.DoesNotContain("⌄", texts);

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_shows_reset_left_of_emergency_with_expected_layout_and_colors()
    {
        var view = new TopBarView { DataContext = new TopBarViewModel() };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var reset = view.FindControl<ToggleButton>("ResetButton")!;
        var emergency = view.FindControl<ToggleButton>("EmergencyButton")!;
        var automatic = view.FindControl<ToggleButton>("AutomaticButton")!;
        var manual = view.FindControl<ToggleButton>("ManualButton")!;

        Assert.Equal("СБРОС", reset.Content);
        Assert.InRange(reset.Bounds.Width, emergency.Bounds.Width - 1, emergency.Bounds.Width + 1);
        Assert.InRange(reset.Bounds.Height, emergency.Bounds.Height - 1, emergency.Bounds.Height + 1);
        Assert.True(reset.Bounds.Right <= emergency.Bounds.Left);
        Assert.Equal(Color.Parse("#ECEFF1"), Assert.IsType<SolidColorBrush>(reset.Background).Color);
        Assert.Equal(Color.Parse("#FF2626"), Assert.IsType<SolidColorBrush>(emergency.Background).Color);
        Assert.Equal(Color.Parse("#CCD3D8"), Assert.IsAssignableFrom<ISolidColorBrush>(automatic.BorderBrush).Color);
        Assert.Equal(Color.Parse("#CCD3D8"), Assert.IsAssignableFrom<ISolidColorBrush>(manual.BorderBrush).Color);
        Assert.Equal(HorizontalAlignment.Center, automatic.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, automatic.VerticalContentAlignment);
        Assert.Equal(HorizontalAlignment.Center, reset.HorizontalContentAlignment);
        Assert.Equal(VerticalAlignment.Center, reset.VerticalContentAlignment);
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(textBlock => textBlock.Text).ToArray();
        var help = view.FindControl<Button>("HelpButton")!;
        Assert.Equal("ПОМОЩЬ", help.Content);
        Assert.DoesNotContain(texts, text => text?.StartsWith("8 ", StringComparison.Ordinal) == true);
        Assert.DoesNotContain("Ожидание", texts);

        window.Close();
    }

    [AvaloniaFact]
    public void Help_dialog_shows_configured_contacts_and_website_link()
    {
        var viewModel = new HelpDialogViewModel(
            new StaticHelpOptionsProvider(new HelpOptions
            {
                Contacts =
                [
                    new() { Label = "Телефон", Value = "8 953 448 31 16" },
                    new() { Label = "E-mail", Value = "example@example.com" },
                    new() { Label = "Сайт", Value = "example.com", Uri = "https://example.com" }
                ]
            }),
            new NoOpExternalLinkLauncher(),
            NullLogger<HelpDialogViewModel>.Instance);
        var view = new HelpDialogView { DataContext = viewModel };
        var window = new Window { Width = 460, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(textBlock => textBlock.Text).ToArray();
        var websiteContact = viewModel.Contacts.Single(contact => contact.Label == "Сайт");
        var website = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => ReferenceEquals(button.Command, websiteContact.OpenLinkCommand));

        Assert.Contains("8 953 448 31 16", texts);
        Assert.Contains("example@example.com", texts);
        Assert.Contains("example.com", texts);
        Assert.Same(websiteContact.OpenLinkCommand, website.Command);

        window.Close();
    }

    [AvaloniaFact]
    public void RouteMap_toggle_buttons_render_fixed_checked_backgrounds()
    {
        var topBarViewModel = new TopBarViewModel();
        topBarViewModel.ApplyRuntime(
            isAutomaticMode: true,
            isManualMode: false,
            isResetActive: true,
            hasEmergency: true,
            isConnectionAvailable: true);
        var topBar = new TopBarView { DataContext = topBarViewModel };
        var topBarWindow = new Window { Width = 1200, Height = 96, Content = topBar };
        topBarWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var automatic = topBar.FindControl<ToggleButton>("AutomaticButton")!;
        var reset = topBar.FindControl<ToggleButton>("ResetButton")!;
        var emergency = topBar.FindControl<ToggleButton>("EmergencyButton")!;
        Assert.Equal(Color.Parse("#003CA3"), Assert.IsAssignableFrom<ISolidColorBrush>(automatic.Background).Color);
        Assert.Equal(Color.Parse("#D1C300"), Assert.IsAssignableFrom<ISolidColorBrush>(reset.Background).Color);
        Assert.Equal(Color.Parse("#D10000"), Assert.IsAssignableFrom<ISolidColorBrush>(emergency.Background).Color);

        topBarViewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            isResetActive: false,
            hasEmergency: true,
            isConnectionAvailable: true);
        Dispatcher.UIThread.RunJobs();
        var manual = topBar.FindControl<ToggleButton>("ManualButton")!;
        Assert.Equal(Color.Parse("#003CA3"), Assert.IsAssignableFrom<ISolidColorBrush>(manual.Background).Color);

        var cardViewModel = new EquipmentCardViewModel(RouteMapSeed.Create().MapEquipment.Single(), new NoOpCommandDispatcher());
        var card = new EquipmentCardView { DataContext = cardViewModel };
        var cardWindow = new Window { Width = 340, Height = 200, Content = card };
        cardWindow.Show();
        Dispatcher.UIThread.RunJobs();

        var start = card.FindControl<ToggleButton>("StartButton")!;
        var stop = card.FindControl<ToggleButton>("StopButton")!;
        var selector = card.FindControl<ToggleButton>("SelectorButton")!;
        cardViewModel.IsSelectorChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#003CA3"), Assert.IsAssignableFrom<ISolidColorBrush>(selector.Background).Color);

        cardViewModel.IsStartChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#00D107"), Assert.IsAssignableFrom<ISolidColorBrush>(start.Background).Color);

        cardViewModel.IsStopChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#D10000"), Assert.IsAssignableFrom<ISolidColorBrush>(stop.Background).Color);

        cardWindow.Close();
        topBarWindow.Close();
    }

    [AvaloniaFact]
    public void TopBar_disables_commands_but_keeps_settings_available_when_connection_is_offline()
    {
        var viewModel = new TopBarViewModel();
        viewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            isResetActive: false,
            hasEmergency: false,
            isConnectionAvailable: false);
        var view = new TopBarView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<ToggleButton>("AutomaticButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("ManualButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("ResetButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("EmergencyButton")!.IsEnabled);

        var settings = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains("settings"));
        Assert.True(settings.IsVisible);
        Assert.True(settings.IsEnabled);

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_applies_enabled_role_per_command_and_keeps_settings_available()
    {
        var viewModel = new TopBarViewModel();
        viewModel.ApplyRuntime(
            isAutomaticMode: false,
            isManualMode: true,
            isResetActive: false,
            hasEmergency: false,
            isConnectionAvailable: true,
            isAutomaticCommandEnabled: false,
            isManualCommandEnabled: true,
            isResetCommandEnabled: false,
            isEmergencyCommandEnabled: false);
        var view = new TopBarView { DataContext = viewModel };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<ToggleButton>("AutomaticButton")!.IsEnabled);
        Assert.True(view.FindControl<ToggleButton>("ManualButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("ResetButton")!.IsEnabled);
        Assert.False(view.FindControl<ToggleButton>("EmergencyButton")!.IsEnabled);
        Assert.True(view.GetVisualDescendants().OfType<Button>().Single(x => x.Classes.Contains("settings")).IsEnabled);

        window.Close();
    }

    [AvaloniaFact]
    public void TopBar_hides_settings_in_user_mode()
    {
        var view = new TopBarView { DataContext = new TopBarViewModel(isSettingsVisible: false) };
        var window = new Window { Width = 1200, Height = 96, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var settings = view.GetVisualDescendants()
            .OfType<Button>()
            .Single(button => button.Classes.Contains("settings"));

        Assert.False(settings.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public void RouteMap_node_menu_items_are_disabled_when_commands_are_disabled()
    {
        var control = new RouteMapControl { AreCommandsEnabled = false };

        var item = control.CreateNodeMenuItem("Отправить", "bsu_1", isChecked: false, command: null);

        Assert.False(item.IsEnabled);
    }

    [AvaloniaFact]
    public void Signal_mapping_groups_are_expanded_by_default_and_can_be_collapsed()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var expanders = fixture.View.GetVisualDescendants()
            .OfType<Expander>()
            .Where(item => item.Classes.Contains("signal-group"))
            .ToArray();

        Assert.NotEmpty(expanders);
        Assert.All(expanders, item => Assert.True(item.IsExpanded));

        var first = expanders[0];
        var group = Assert.IsType<RouteMapSignalMappingGroup>(first.DataContext);
        first.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();

        Assert.False(first.IsExpanded);
        Assert.False(group.IsExpanded);
    }

    [AvaloniaFact]
    public void Signal_mapping_uses_gray_background_only_for_unmapped_rows()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowBorders = fixture.View.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("signal-mapping-row"))
            .ToArray();

        Assert.NotEmpty(rowBorders);
        var unmapped = rowBorders.First(border =>
            border.DataContext is RouteMapSignalMappingRow { IsSystem: false, IsMapped: false });
        var brush = Assert.IsType<SolidColorBrush>(unmapped.Background);
        Assert.Equal(Color.Parse("#F1F3F5"), brush.Color);

        Assert.All(
            rowBorders.Where(border => border.DataContext is RouteMapSignalMappingRow { IsSystem: true }),
            border => Assert.Same(Brushes.White, border.Background));
    }

    [AvaloniaFact]
    public void Signal_mapping_physical_addresses_are_editable_text_boxes()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = FirstSignalMappingRowGrid(fixture.View);

        var addressInputs = rowGrid.Children
            .OfType<TextBox>()
            .Where(textBox => Grid.GetColumn(textBox) is 13 or 14)
            .ToArray();

        Assert.Equal(2, addressInputs.Length);
        Assert.All(addressInputs, input => Assert.NotNull(input.ContextMenu));
    }

    [AvaloniaFact]
    public void Signal_mapping_interactive_row_controls_have_context_menus()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = FirstSignalMappingRowGrid(fixture.View);
        var actionGrid = rowGrid.Children
            .OfType<Grid>()
            .Single(grid => Grid.GetColumn(grid) == 15);
        var controls = rowGrid.Children
            .OfType<Control>()
            .Concat(actionGrid.Children.OfType<Control>())
            .Where(control => control.IsVisible)
            .Where(control => control is ComboBox or TextBox or Button)
            .ToArray();

        Assert.NotEmpty(controls);
        Assert.All(controls, control => Assert.NotNull(control.ContextMenu));
    }

    [AvaloniaFact]
    public void Signal_mapping_node_register_address_enables_bit_input()
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = SignalMappingRowGrid(fixture.View, "bsu_1.fault");
        var row = Assert.IsType<RouteMapSignalMappingRow>(rowGrid.DataContext);
        row.IsMapped = true;

        row.ClientPhysicalAddressText = "16420";
        Dispatcher.UIThread.RunJobs();

        var bitInput = rowGrid.Children
            .OfType<TextBox>()
            .Single(textBox => Grid.GetColumn(textBox) == 10);
        Assert.True(bitInput.IsEnabled);
    }

    [AvaloniaTheory]
    [InlineData("active_bsu1_bsu2.fault")]
    [InlineData("route.bsu2_to_bucket.fragment_1.active")]
    [InlineData("equip.bucket.start")]
    public void Signal_mapping_line_and_card_register_address_enables_bit_input(string signalId)
    {
        using var fixture = new SignalMappingFixture(1200, 760);
        var rowGrid = SignalMappingRowGrid(fixture.View, signalId);
        var row = Assert.IsType<RouteMapSignalMappingRow>(rowGrid.DataContext);
        row.IsMapped = true;

        row.ClientPhysicalAddressText = "16420";
        Dispatcher.UIThread.RunJobs();

        var bitInput = rowGrid.Children
            .OfType<TextBox>()
            .Single(textBox => Grid.GetColumn(textBox) == 10);
        Assert.True(bitInput.IsEnabled);
    }

    [AvaloniaFact]
    public void Modbus_settings_data_map_rows_scroll_horizontally_without_overlap()
    {
        var options = new ModbusOptions
        {
            DataMap =
            [
                new ModbusDataPointOptions
                {
                    Name = "route.node.bsu_1.target.off",
                    Area = ModbusDataArea.Coil,
                    Address = 0,
                    Length = 1,
                    Access = ModbusDataAccess.Read,
                    Type = ModbusValueType.Bool,
                }
            ]
        };
        var viewModel = new ModbusSettingsDialogViewModel(
            "Настройки Modbus TCP Demo",
            ModbusOptions.DemoSectionName,
            options,
            new NullAppConfigService(),
            new ModbusDataMapValidator());
        var view = new ModbusSettingsDialogView { DataContext = viewModel };
        var window = new Window { Width = 900, Height = 520, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var tabs = view.GetVisualDescendants().OfType<TabControl>().Single();
        tabs.SelectedIndex = 3;
        Dispatcher.UIThread.RunJobs();

        var row = view.GetVisualDescendants()
            .OfType<Grid>()
            .Single(item => item.Classes.Contains("modbus-data-point-row"));
        var controls = row.Children.OfType<Control>().ToArray();

        Assert.Equal(10, controls.Length);
        for (var index = 1; index < controls.Length; index++)
            Assert.True(controls[index - 1].Bounds.Right <= controls[index].Bounds.Left);

        Assert.Contains(
            view.GetVisualDescendants().OfType<ScrollViewer>(),
            scroll => scroll.Extent.Width > scroll.Viewport.Width);

        window.Close();
    }

    private static Grid FirstSignalMappingRowGrid(RouteMapSignalMappingView view)
        => view.GetVisualDescendants()
            .OfType<Grid>()
            .First(grid =>
                grid.DataContext is RouteMapSignalMappingRow
                && grid.Children.OfType<Control>().Any(control => Grid.GetColumn(control) == 13)
                && grid.Children.OfType<Control>().Any(control => Grid.GetColumn(control) == 14)
                && grid.Children.OfType<Grid>().Any(control => Grid.GetColumn(control) == 15));

    private static Grid SignalMappingRowGrid(RouteMapSignalMappingView view, string signalId)
        => view.GetVisualDescendants()
            .OfType<Grid>()
            .Single(grid =>
                grid.DataContext is RouteMapSignalMappingRow row
                && row.SignalId == signalId
                && grid.Children.OfType<Grid>().Any(control => Grid.GetColumn(control) == 15));

    private static RouteMapDashboardViewModel CreateRouteMapDashboardViewModel(
        RouteMapConfigurationManager manager,
        NotificationsPanelViewModel notificationsPanel,
        string workMode)
    {
        return new RouteMapDashboardViewModel(
            manager,
            new EmptySignalProvider(),
            new RouteMapRuntimeMapper(manager.CurrentDefinition),
            new NoOpCommandDispatcher(),
            new NoOpSettingsDialogService(),
            notificationsPanel,
            new RouteMapSessionJournal(),
            new StaticOptionsMonitor(new ModbusOptions()),
            Options.Create(new ApplicationOptions { WorkMode = workMode }));
    }

    private static string TextOf(TextBlock textBlock) =>
        string.Concat((textBlock.Inlines?.OfType<Run>() ?? Enumerable.Empty<Run>()).Select(run => run.Text));

    private sealed class DialogFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "route-map-ui-tests", Guid.NewGuid().ToString("N"));
        private readonly RouteMapConfigurationManager _manager;
        private readonly RouteMapSettingsViewModel _viewModel;

        public DialogFixture(double width, double height)
        {
            Directory.CreateDirectory(_directory);
            var storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            var mapper = new RouteMapConfigurationMapper(RouteMapSeed.Create());
            _manager = new RouteMapConfigurationManager(
                storage,
                mapper,
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            _viewModel = new RouteMapSettingsViewModel(_manager, storage, new NullFilePicker());
            Dialog = new RouteMapSettingsDialog { DataContext = _viewModel };
            Window = new Window { Width = width, Height = height, Content = Dialog };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public Window Window { get; }
        public RouteMapSettingsDialog Dialog { get; }
        public RouteMapConfigurationManager Manager => _manager;
        public RouteMapSettingsViewModel ViewModel => _viewModel;

        public void SelectTab(int index)
        {
            var tabs = Dialog.GetVisualDescendants().OfType<TabControl>().Single();
            tabs.SelectedIndex = index;
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Window.Close();
            _viewModel.Dispose();
            _manager.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class SignalMappingFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "route-map-mapping-ui-tests", Guid.NewGuid().ToString("N"));
        private readonly RouteMapConfigurationManager _manager;
        private readonly RouteMapSignalMappingViewModel _viewModel;

        public SignalMappingFixture(double width, double height)
        {
            Directory.CreateDirectory(_directory);
            var storage = new RouteMapConfigurationStorage(Path.Combine(_directory, "route-map.json"));
            _manager = new RouteMapConfigurationManager(
                storage,
                new RouteMapConfigurationMapper(RouteMapSeed.Create()),
                new RouteMapConfigurationValidator(),
                new RouteMapConfigurationMigrator());
            var options = new ModbusOptions
            {
                Client = new ModbusEndpointOptions
                {
                    CoilStartAddress = 0,
                    CoilCount = 2000,
                    HoldingRegisterStartAddress = 16384,
                    RegisterCount = 100
                },
                Server = new ModbusEndpointOptions
                {
                    CoilStartAddress = 0,
                    CoilCount = 2000,
                    HoldingRegisterStartAddress = 16384,
                    RegisterCount = 100
                }
            };
            _viewModel = new RouteMapSignalMappingViewModel(
                _manager,
                new StaticOptionsMonitor(options),
                new NullAppConfigService(),
                new ModbusDataMapValidator(),
                new NoOpDataMapRuntime());
            View = new RouteMapSignalMappingView { DataContext = _viewModel };
            Window = new Window { Width = width, Height = height, Content = View };
            Window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public RouteMapSignalMappingView View { get; }
        public Window Window { get; }

        public void Dispose()
        {
            Window.Close();
            _viewModel.Dispose();
            _manager.Dispose();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class StaticOptionsMonitor(ModbusOptions value) : IOptionsMonitor<ModbusOptions>
    {
        public ModbusOptions CurrentValue { get; } = value;
        public ModbusOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<ModbusOptions, string?> listener) => null;
    }

    private sealed class NoOpDataMapRuntime : IModbusDataMapRuntime
    {
        public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap) =>
            ModbusOperationResult.Success();
    }

    private sealed class EmptySignalProvider : ISignalValueProvider
    {
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() =>
            Observable.Empty<IReadOnlyDictionary<string, SignalValue>>();
    }

    private sealed class NoOpCommandDispatcher : IEquipmentCommandDispatcher
    {
        public Task DispatchAsync(SignalWriteRequest request, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpExternalLinkLauncher : IExternalLinkLauncher
    {
        public Task OpenAsync(Uri uri, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StaticHelpOptionsProvider(HelpOptions options) : IHelpOptionsProvider
    {
        public HelpOptions GetCurrent() => options;
    }

    private sealed class NoOpSettingsDialogService : IRouteMapSettingsDialogService
    {
        public Task ShowAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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

    private sealed class NullAppConfigService : IAppConfigService
    {
        public T GetSection<T>(string sectionName) where T : class, new() => new();
        public string GetValue(string key) => string.Empty;
        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default) => Task.CompletedTask;
        public void SaveUserSettings(UserSettings settings) { }
        public UserSettings LoadUserSettings() => new();
    }

    private sealed class NullFilePicker : IRouteMapSettingsFilePicker
    {
        public Task<string?> PickImportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
        public Task<string?> PickExportPathAsync(CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private static WorkspaceViewModel CreateWorkspaceViewModel(bool isAdminMode)
    {
        var runtime = new NoOpModbusRuntime();
        var optionsMonitor = new StaticOptionsMonitor(new ModbusOptions());
        return new WorkspaceViewModel(
            hostScreen: null!,
            authService: new TestAuthApp(),
            modbusDemo: null!,
            alarmManager: new AlarmManagerViewModel(
                optionsMonitor,
                new NullAppConfigService(),
                new ModbusAlarmMapValidator()),
            alarmMonitor: new ModbusAlarmMonitor(
                optionsMonitor,
                runtime,
                new NoOpDialogService(),
                new NoOpBitWriter(),
                new RouteMapSessionJournal(),
                NullLogger<ModbusAlarmMonitor>.Instance),
            routeMapDashboard: null!,
            routeMapSignalMapping: null!,
            modbusRuntime: runtime,
            modbusOptions: new StaticModbusDemoOptionsProvider(),
            applicationOptions: Options.Create(new ApplicationOptions
            {
                WorkMode = isAdminMode ? ApplicationOptions.AdminWorkMode : ApplicationOptions.UserWorkMode
            }),
            logger: NullLogger<WorkspaceViewModel>.Instance);
    }

    private sealed class TestAuthApp : IAuthApp
    {
        public bool IsAuthenticated { get; set; } = true;
        public bool Authenticate(string username, string password) => IsAuthenticated;
    }

    private sealed class StaticModbusDemoOptionsProvider : IModbusDemoOptionsProvider
    {
        public ModbusOptions CurrentValue { get; } = new();
    }

    private sealed class NoOpModbusRuntime : IModbusRuntimeService
    {
        public ModbusStatus Status => ModbusStatus.Stopped;
        public ModbusSnapshot ClientSnapshot => ModbusSnapshot.Empty;
        public ModbusSnapshot ServerSnapshot => ModbusSnapshot.Empty;
        public ModbusOptions CurrentOptions { get; } = new();
        public event EventHandler<ModbusStatus>? StatusChanged { add { } remove { } }
        public event EventHandler<ModbusSnapshot>? SnapshotChanged { add { } remove { } }
        public Task StartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartAsync(ModbusRunMode mode = ModbusRunMode.Both, ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopClientAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartClientAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task StopServerAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task RestartServerAsync(ModbusOptions? options = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
