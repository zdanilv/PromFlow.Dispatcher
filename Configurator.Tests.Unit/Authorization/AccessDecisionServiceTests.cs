using Configurator.Application.Services.Authorization;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class AccessDecisionServiceTests
{
    [Fact]
    public void Authorize_DeniesAnonymousSession()
    {
        var accessor = new TestSessionAccessor();
        var service = new DefaultAccessDecisionService(accessor, new NoLicenseFeatureGate());

        var decision = service.Authorize(new AccessRequirement(Permission.ViewRouteMap));

        Assert.False(decision.Succeeded);
        Assert.Equal("NotAuthenticated", decision.ReasonCode);
    }

    [Fact]
    public void Authorize_AllowsUserPermissionAndDeniesMissingPermission()
    {
        var accessor = new TestSessionAccessor();
        accessor.SetCurrent(CreateSession(
            UserRole.User,
            Permission.ViewRouteMap,
            Permission.IssueEquipmentCommands));
        var service = new DefaultAccessDecisionService(accessor, new NoLicenseFeatureGate());

        var allowed = service.Authorize(new AccessRequirement(Permission.ViewRouteMap));
        var denied = service.Authorize(new AccessRequirement(Permission.ViewSignalMapping));

        Assert.True(allowed.Succeeded);
        Assert.False(denied.Succeeded);
        Assert.Equal("PermissionDenied", denied.ReasonCode);
    }

    [Fact]
    public void Authorize_AllowsAdministratorPermissionButDeniesUnavailableLicenseFeature()
    {
        var accessor = new TestSessionAccessor();
        accessor.SetCurrent(CreateSession(UserRole.Administrator, Enum.GetValues<Permission>()));
        var service = new DefaultAccessDecisionService(accessor, new NoLicenseFeatureGate());

        var permissionAllowed = service.Authorize(new AccessRequirement(Permission.ManageUsers));
        var featureDenied = service.Authorize(new AccessRequirement(Permission.ManageUsers, "CommercialFeature"));

        Assert.True(permissionAllowed.Succeeded);
        Assert.False(featureDenied.Succeeded);
        Assert.Equal("LicenseFeatureUnavailable", featureDenied.ReasonCode);
    }

    private static UserSession CreateSession(UserRole role, params Permission[] permissions) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            role == UserRole.Administrator ? "admin" : "operator",
            role,
            permissions,
            DateTimeOffset.UtcNow);

    private sealed class TestSessionAccessor : IUserSessionAccessor
    {
        public UserSessionSnapshot Current { get; private set; } = UserSessionSnapshot.Anonymous;

        public void SetCurrent(UserSession session) =>
            Current = UserSessionSnapshot.Authenticated(session);

        public void Clear() => Current = UserSessionSnapshot.Anonymous;
    }
}
