using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using ReactiveUI.Avalonia;

[assembly: AvaloniaTestApplication(typeof(Configurator.Tests.RouteMap.Ui.HeadlessTestApplication))]

namespace Configurator.Tests.RouteMap.Ui;

public static class HeadlessTestApplication
{
    internal static string? ScreenshotDirectory => Environment.GetEnvironmentVariable("PROMFLOW_ROUTEMAP_SCREENSHOTS");

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<TestApplication>();
        var captureScreenshots = !string.IsNullOrEmpty(ScreenshotDirectory);
        if (captureScreenshots)
            builder.UseSkia();
        return builder
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = !captureScreenshots })
            .UseReactiveUI(_ => { });
    }
}

internal sealed class TestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }
}
