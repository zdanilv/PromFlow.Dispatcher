namespace Configurator.Application.Services.Licensing;

public interface ILicenseStateAccessor
{
    LicenseState Current { get; }

    event EventHandler<LicenseStateChangedEventArgs>? StateChanged;
}
