namespace Configurator.Application.Services.Licensing;

public sealed record InstallationIdentity(
    string InstallationId,
    DateTimeOffset CreatedAtUtc);

public sealed record InstallationIdentityRequest(
    string Format,
    string Product,
    string InstallationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset GeneratedAtUtc);
