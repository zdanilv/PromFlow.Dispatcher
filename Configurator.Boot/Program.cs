using Avalonia;
using Configurator.Application;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Signals;
using Configurator.Desktop;
using Configurator.Desktop.Dialogs;
using Configurator.Desktop.Dialogs.ConfirmDialog;
using Configurator.Desktop.Dialogs.InputDialog;
using Configurator.Desktop.Dialogs.ModbusSettingsDialog;
using Configurator.Desktop.Dialogs.OpcUaTagEditorDialog;
using Configurator.Desktop.Dialogs.OpcUaTagImportDialog;
using Configurator.Desktop.Main;
using Configurator.Desktop.Workspace;
using Configurator.Desktop.Workspace.Authorization;
using Configurator.Desktop.Workspace.ModbusDemo;
using Configurator.Desktop.Workspace.OpcUa;
using Configurator.Desktop.Workspace.RouteMap;
using Configurator.Desktop.Workspace.RouteMap.Configuration;
using Configurator.Desktop.Workspace.RouteMap.Models;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Configurator.Desktop.Workspace.RouteMap.SignalMapping;
using Configurator.Desktop.Workspace.RouteMap.Settings;
using Configurator.Desktop.Workspace.RouteMap.ViewModels;
using Configurator.Infrastructure;
using Configurator.Infrastructure.Modbus;
using Configurator.Infrastructure.Modbus.RouteMap;
using Configurator.Infrastructure.OpcUa;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Avalonia;
using ReactiveUI.Avalonia.Splat;
using Serilog;
using Serilog.Events;
using Splat;
using Splat.Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace Configurator.Boot;

/// <summary>
/// Точка входа Avalonia-приложения и место сборки DI-контейнера desktop-оболочки.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Запускает desktop-приложение в классическом жизненном цикле Avalonia.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Настраивает Avalonia, ReactiveUI и зависимости, включая Modbus/OpcUa инфраструктуру.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        // Конфигурация нужна инфраструктурным модулям Modbus/OpcUa для начальных настроек.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        return AppBuilder
            .Configure<Configurator.Desktop.App>()
            .UsePlatformDetect()
            .UseReactiveUIWithMicrosoftDependencyResolver(
                services =>
                {
                    services.AddLogging();
                    services.AddApplication();
                    services.AddInfrastructure(configuration);
                    // Modbus/OpcUa регистрируются в boot-слое, чтобы Desktop зависел только от application-контрактов.
                    services.AddModbusInfrastructure(configuration);
                    services.AddOpcUaInfrastructure(configuration);

                    services.AddSingleton(sp => new RouteMapConfigurationMapper(RouteMapSeed.Create()));
                    services.AddSingleton<RouteMapConfigurationStorage>();
                    services.AddSingleton<RouteMapConfigurationValidator>();
                    services.AddSingleton<RouteMapConfigurationMigrator>();
                    services.AddSingleton<RouteMapConfigurationManager>();
                    services.AddSingleton<IRouteMapSettingsFilePicker, RouteMapSettingsFilePicker>();
                    services.AddSingleton<IRouteMapSettingsDialogService, RouteMapSettingsDialogService>();
                    services.AddTransient<RouteMapSettingsViewModel>();
                    services.AddTransient<RouteMapSettingsDialog>();
                    services.AddSingleton<IRouteMapRuntimeMapper<RouteMapRuntimeState>, RouteMapRuntimeMapper>();
                    services.AddTransient<RouteMapDashboardViewModel>();
                    services.AddTransient<RouteMapSignalMappingViewModel>();

                    var routeMapRuntime = configuration
                        .GetSection(RouteMapRuntimeOptions.SectionName)
                        .Get<RouteMapRuntimeOptions>() ?? new RouteMapRuntimeOptions();
                    services.AddSingleton<MockSignalState>();
                    services.AddSingleton<MockSignalProvider>();
                    services.AddSingleton<MockEquipmentCommandDispatcher>();
                    services.AddSingleton<ModbusTcpSignalValueProvider>();
                    services.AddSingleton<ModbusTcpCommandDispatcher>();
                    services.AddSingleton<IRouteMapSignalRuntime>(sp => new RouteMapSignalRuntime(
                        sp.GetRequiredService<MockSignalProvider>(),
                        sp.GetRequiredService<MockEquipmentCommandDispatcher>(),
                        sp.GetRequiredService<ModbusTcpSignalValueProvider>(),
                        sp.GetRequiredService<ModbusTcpCommandDispatcher>(),
                        routeMapRuntime.SignalSource));
                    services.AddSingleton<ISignalValueProvider>(sp => sp.GetRequiredService<IRouteMapSignalRuntime>());
                    services.AddSingleton<IEquipmentCommandDispatcher>(sp => sp.GetRequiredService<IRouteMapSignalRuntime>());
                    services.AddTransient<RouteMapModbusBindingDiagnostics>();

                    services.AddSingleton<Configurator.Desktop.Main.MainWindow>();
                    services.AddSingleton<Configurator.Desktop.Main.MainViewModel>();

                    services.AddTransient<AuthorizationViewModel>();
                    services.AddTransient<WorkspaceViewModel>();
                    services.AddTransient<Configurator.Desktop.Workspace.Modbus.ModbusViewModel>();
                    services.AddTransient<ModbusDemoViewModel>();
                    services.AddTransient<OpcUaViewModel>();
                    services.AddTransient<Func<IScreen, WorkspaceViewModel>>(sp =>
                        hostScreen => ActivatorUtilities.CreateInstance<WorkspaceViewModel>(sp, hostScreen));

                    services.AddTransient<IViewFor<WorkspaceViewModel>, WorkspaceView>();
                    services.AddTransient<IViewFor<AuthorizationViewModel>, AuthorizationView>();
                    services.AddTransient<IViewFor<Configurator.Desktop.Workspace.Modbus.ModbusViewModel>, Configurator.Desktop.Workspace.Modbus.ModbusView>();
                    services.AddTransient<IViewFor<ModbusDemoViewModel>, ModbusDemoView>();
                    services.AddTransient<IViewFor<OpcUaViewModel>, OpcUaView>();
                    services.AddTransient<RouteMapDashboardView>();
                    services.AddTransient<RouteMapSignalMappingView>();

                    services.AddTransient<ConfirmDialogView>();
                    services.AddTransient<InputDialogView>();
                    services.AddTransient<ModbusSettingsDialogView>();
                    services.AddTransient<OpcUaTagEditorDialogView>();
                    services.AddTransient<OpcUaTagImportDialogView>();

                    services.AddSingleton<IDialogViewFactory, DialogViewFactory>();
                    services.AddSingleton<DialogCoordinator>();

                    services.AddSingleton<IDialogService, DialogHostDialogService>();

                },
                withResolver: sp =>
                {
                    // App использует ServiceProvider при shutdown, чтобы остановить runtime-сервисы.
                    Configurator.Desktop.App.Services = sp!;
                }
            );
    }
}
