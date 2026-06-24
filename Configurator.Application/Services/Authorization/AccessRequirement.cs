namespace Configurator.Application.Services.Authorization;

public sealed record AccessRequirement(
    Permission RequiredPermission,
    string? RequiredLicenseFeature = null);
