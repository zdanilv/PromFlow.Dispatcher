using Configurator.Application.Services.Authorization;

namespace Configurator.Desktop.Workspace;

public sealed record WorkspaceTabDescriptor(
    string Id,
    string Header,
    Permission RequiredPermission,
    string? RequiredLicenseFeature,
    Func<IServiceProvider, object> Factory,
    int Order);
