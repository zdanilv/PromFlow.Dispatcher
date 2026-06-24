using Avalonia;
using Configurator.Application;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Dialogs;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Contracts;
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
using Configurator.Desktop.Workspace.Archive;
using Configurator.Desktop.Workspace.Authorization;
using Configurator.Desktop.Workspace.Licensing;
using Configurator.Desktop.Workspace.ModbusDemo;
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
using Configurator.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
                    services.AddPersistenceInfrastructure(configuration);
                    // Modbus/OpcUa регистрируются в boot-слое, чтобы Desktop зависел только от application-контрактов.
                    services.AddModbusInfrastructure(configuration);
                    services.Replace(ServiceDescriptor.Singleton<IModbusDataMapRuntime>(sp =>
                        new AuthorizedModbusDataMapRuntime(
                            (IModbusDataMapRuntime)sp.GetRequiredService<IModbusTcpService>(),
                            sp.GetRequiredService<IAccessDecisionService>())));
                    services.AddOpcUaInfrastructure(configuration);

                    services.AddSingleton(sp => new RouteMapConfigurationMapper(RouteMapSeed.Create()));
                    services.AddSingleton<RouteMapConfigurationStorage>();
                    services.AddSingleton<RouteMapConfigurationValidator>();
                    services.AddSingleton<RouteMapConfigurationMigrator>();
                    services.AddSingleton<RouteMapConfigurationManager>();
                    services.AddSingleton<IRouteMapConfigurationMutationService, AuthorizedRouteMapConfigurationMutationService>();
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
                        sp.GetRequiredService<IAccessDecisionService>(),
                        routeMapRuntime.SignalSource));
                    services.AddSingleton<ISignalValueProvider>(sp => sp.GetRequiredService<IRouteMapSignalRuntime>());
                    services.AddSingleton<IEquipmentCommandDispatcher>(sp => sp.GetRequiredService<IRouteMapSignalRuntime>());
                    services.AddTransient<RouteMapModbusBindingDiagnostics>();

                    services.AddSingleton<Configurator.Desktop.Main.MainWindow>();
                    services.AddSingleton<Configurator.Desktop.Main.MainViewModel>();

                    services.AddTransient<AuthorizationViewModel>();
                    services.AddTransient<AdminBootstrapViewModel>();
                    services.AddTransient<WorkspaceViewModel>();
                    services.AddTransient<ArchiveViewModel>();
                    services.AddTransient<LicenseViewModel>();
                    services.AddTransient<ModbusDemoViewModel>();
                    services.AddSingleton<IArchiveFilePicker, ArchiveFilePicker>();
                    services.AddSingleton<ILicenseFilePicker, LicenseFilePicker>();
                    services.AddTransient<Func<IScreen, Func<CancellationToken, Task>, AuthorizationViewModel>>(sp =>
                        (hostScreen, onSucceeded) => ActivatorUtilities.CreateInstance<AuthorizationViewModel>(
                            sp,
                            hostScreen,
                            onSucceeded));
                    services.AddTransient<Func<IScreen, Func<CancellationToken, Task>, AdminBootstrapViewModel>>(sp =>
                        (hostScreen, onSucceeded) => ActivatorUtilities.CreateInstance<AdminBootstrapViewModel>(
                            sp,
                            hostScreen,
                            onSucceeded));
                    services.AddTransient<Func<IScreen, Func<WorkspaceViewModel, CancellationToken, Task>, WorkspaceViewModel>>(sp =>
                        (hostScreen, onLogout) => ActivatorUtilities.CreateInstance<WorkspaceViewModel>(
                            sp,
                            hostScreen,
                            onLogout));

                    services.AddSingleton<WorkspaceTabDescriptor>(_ => new WorkspaceTabDescriptor(
                        "route-map",
                        "Route Map",
                        Permission.ViewRouteMap,
                        LicenseFeature.RouteMap,
                        serviceProvider => serviceProvider.GetRequiredService<RouteMapDashboardViewModel>(),
                        0));
                    services.AddSingleton<WorkspaceTabDescriptor>(_ => new WorkspaceTabDescriptor(
                        "signal-map",
                        "SignalId ↔ Modbus",
                        Permission.ViewSignalMapping,
                        LicenseFeature.EngineeringTools,
                        serviceProvider => serviceProvider.GetRequiredService<RouteMapSignalMappingViewModel>(),
                        10));
                    services.AddSingleton<WorkspaceTabDescriptor>(_ => new WorkspaceTabDescriptor(
                        "modbus-demo",
                        "Modbus Demo",
                        Permission.ViewModbusDiagnostics,
                        LicenseFeature.Diagnostics,
                        serviceProvider => serviceProvider.GetRequiredService<ModbusDemoViewModel>(),
                        20));
                    services.AddSingleton<WorkspaceTabDescriptor>(_ => new WorkspaceTabDescriptor(
                        "archive",
                        "Archive",
                        Permission.ViewArchive,
                        LicenseFeature.Archive,
                        serviceProvider => serviceProvider.GetRequiredService<ArchiveViewModel>(),
                        25));
                    services.AddSingleton<WorkspaceTabDescriptor>(_ => new WorkspaceTabDescriptor(
                        "license",
                        "License",
                        Permission.ViewLicense,
                        null,
                        serviceProvider => serviceProvider.GetRequiredService<LicenseViewModel>(),
                        30));

                    services.AddTransient<IViewFor<WorkspaceViewModel>, WorkspaceView>();
                    services.AddTransient<IViewFor<AuthorizationViewModel>, AuthorizationView>();
                    services.AddTransient<IViewFor<AdminBootstrapViewModel>, AdminBootstrapView>();
                    services.AddTransient<IViewFor<ArchiveViewModel>, ArchiveView>();
                    services.AddTransient<IViewFor<LicenseViewModel>, LicenseView>();
                    services.AddTransient<IViewFor<ModbusDemoViewModel>, ModbusDemoView>();
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
