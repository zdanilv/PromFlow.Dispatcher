using ReactiveUI.Builder;

namespace Configurator.Tests.Unit;

internal static class ReactiveUiTestSetup
{
    private static readonly Lazy<bool> Initialized = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder().WithCoreServices().BuildApp();
        return true;
    });

    public static void EnsureInitialized() => _ = Initialized.Value;
}
