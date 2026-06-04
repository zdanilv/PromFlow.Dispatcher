using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using ReactiveUI.Avalonia;
using ReactiveUI.Builder;

[assembly: AvaloniaTestApplication(typeof(Configurator.Infrastructure.Modbus.Tests.UI.ModbusDemoHeadlessApp))]

namespace Configurator.Infrastructure.Modbus.Tests.UI;

public static class ModbusDemoHeadlessApp
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<ModbusDemoTestApplication>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseReactiveUI(static builder => builder.WithCoreServices());
}

internal sealed class ModbusDemoTestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
