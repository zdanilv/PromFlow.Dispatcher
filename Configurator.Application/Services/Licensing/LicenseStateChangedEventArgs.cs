namespace Configurator.Application.Services.Licensing;

public sealed class LicenseStateChangedEventArgs : EventArgs
{
    public LicenseStateChangedEventArgs(LicenseState previous, LicenseState current)
    {
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    public LicenseState Previous { get; }

    public LicenseState Current { get; }
}
