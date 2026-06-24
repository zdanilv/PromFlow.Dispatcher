using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Desktop.Workspace.Licensing;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class LicenseViewVisualTests
{
    [AvaloniaFact]
    public void LicenseView_RendersStatusAndActions()
    {
        using var viewModel = new LicenseViewModel(
            new FakeLicenseService(),
            new FakeLicenseService(),
            new FakeInstallationIdentityService(),
            new FakeRequestExportService(),
            new FakePicker(),
            new AllowAccessDecisionService(),
            new LicensingOptions());
        var view = new LicenseView { DataContext = viewModel };
        var window = new Window { Width = 960, Height = 640, Content = view };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(text => text.Text).ToArray();
        Assert.Contains("License", texts);
        Assert.Contains(LicenseStatus.Valid.ToString(), texts);
        Assert.Equal(3, view.GetVisualDescendants().OfType<Button>().Count());
        window.Close();
    }

    private sealed class FakeLicenseService : ILicenseService, ILicenseStateAccessor
    {
        public LicenseState Current { get; } = new(
            LicenseStatus.Valid,
            DateTimeOffset.UtcNow,
            new LicensePayload
            {
                LicenseId = Guid.NewGuid(),
                Product = LicenseConstants.Product,
                IssuedAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ValidFromUtc = DateTimeOffset.UtcNow.AddDays(-1),
                ExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
                Edition = LicenseEdition.Professional,
                LicenseVersion = LicenseConstants.LicenseVersion,
                ProductVersion = new LicenseProductVersionRange(),
                Features = [LicenseFeature.RouteMap, LicenseFeature.RemoteControl],
                Organization = new LicenseOrganizationProfile { Name = "Org", SiteAddress = "Site" },
                Customer = new LicenseCustomerProfile { FullName = "Customer" },
                Installation = new LicenseInstallationProfile
                {
                    BindingMode = LicenseInstallationBindingMode.InstallationId,
                    InstallationId = "installation-1"
                }
            },
            []);

        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged { add { } remove { } }

        public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task<LicenseInstallResult> InstallAsync(
            LicenseInstallRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeInstallationIdentityService : IInstallationIdentityService
    {
        public Task<InstallationIdentity> GetOrCreateAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstallationIdentity("installation-1", DateTimeOffset.UnixEpoch));

        public Task<InstallationIdentityRequest> CreateRequestAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstallationIdentityRequest(
                LicenseConstants.RequestFormat,
                LicenseConstants.Product,
                "installation-1",
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch));
    }

    private sealed class FakeRequestExportService : ILicenseRequestExportService
    {
        public Task<LicenseRequestExportResult> ExportAsync(
            InstallationIdentityRequest request,
            string path,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(LicenseRequestExportResult.Success(path));
    }

    private sealed class FakePicker : ILicenseFilePicker
    {
        public Task<string?> PickLicensePathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> PickRequestExportPathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class AllowAccessDecisionService : IAccessDecisionService
    {
        private static readonly UserSession Session = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin",
            UserRole.Administrator,
            Enum.GetValues<Permission>(),
            DateTimeOffset.UtcNow);

        public AccessDecision Authorize(AccessRequirement requirement) =>
            AccessDecision.Allow(requirement, Session);

        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Authorize(requirement));
    }
}
