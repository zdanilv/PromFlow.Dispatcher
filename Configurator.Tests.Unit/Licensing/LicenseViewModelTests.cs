using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Desktop.Workspace.Licensing;
using System.Reactive.Threading.Tasks;
using Xunit;

namespace Configurator.Tests.Unit.Licensing;

public sealed class LicenseViewModelTests : IDisposable
{
    private readonly string _directoryPath = Path.Combine(
        Path.GetTempPath(),
        "PromFlow.LicenseViewModelTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallCommand_PickerCancellationDoesNotCallService()
    {
        var fixture = new Fixture();
        using var viewModel = fixture.CreateViewModel();

        await viewModel.InstallCommand.Execute().ToTask();

        Assert.Equal(0, fixture.LicenseService.InstallCount);
        Assert.Null(viewModel.ErrorMessage);
    }

    [Fact]
    public async Task InstallCommand_ValidSelectionInstallsAndUpdatesStatus()
    {
        Directory.CreateDirectory(_directoryPath);
        var licensePath = Path.Combine(_directoryPath, "valid.promlicense");
        await File.WriteAllBytesAsync(licensePath, [1, 2, 3]);
        var fixture = new Fixture { LicensePath = licensePath };
        using var viewModel = fixture.CreateViewModel();

        await viewModel.InstallCommand.Execute().ToTask();

        Assert.Equal(1, fixture.LicenseService.InstallCount);
        Assert.Equal(LicenseStatus.Valid.ToString(), viewModel.Status);
        Assert.Equal("License installed.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task ExportRequestCommand_WritesRequestAndShowsInstallationId()
    {
        var requestPath = Path.Combine(_directoryPath, "installation.promrequest");
        var fixture = new Fixture { RequestPath = requestPath };
        using var viewModel = fixture.CreateViewModel();

        await viewModel.ExportRequestCommand.Execute().ToTask();

        Assert.Equal("installation-1", viewModel.InstallationId);
        Assert.Contains(requestPath, viewModel.StatusMessage);
        Assert.Equal(1, fixture.ExportService.ExportCount);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directoryPath))
            {
                Directory.Delete(_directoryPath, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class Fixture
    {
        public string? LicensePath { get; set; }
        public string? RequestPath { get; set; }
        public FakeLicenseService LicenseService { get; } = new();
        public FakeExportService ExportService { get; } = new();

        public LicenseViewModel CreateViewModel()
        {
            var picker = new FakePicker { LicensePath = LicensePath, RequestPath = RequestPath };
            return new LicenseViewModel(
                LicenseService,
                LicenseService,
                new FakeInstallationIdentityService(),
                ExportService,
                picker,
                new AllowAccessDecisionService(),
                new LicensingOptions());
        }
    }

    private sealed class FakeLicenseService : ILicenseService, ILicenseStateAccessor
    {
        public int InstallCount { get; private set; }
        public LicenseState Current { get; private set; } = LicenseState.Missing(DateTimeOffset.UnixEpoch);
        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged;

        public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);

        public Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Current);
        }

        public Task<LicenseInstallResult> InstallAsync(
            LicenseInstallRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallCount++;
            var previous = Current;
            Current = ValidState();
            StateChanged?.Invoke(this, new LicenseStateChangedEventArgs(previous, Current));

            return Task.FromResult(LicenseInstallResult.Success(Current, null, Current.Payload!.LicenseId.ToString("D")));
        }

        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static LicenseState ValidState()
            => new(
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
                    Features = [LicenseFeature.RouteMap],
                    Installation = new LicenseInstallationProfile
                    {
                        BindingMode = LicenseInstallationBindingMode.InstallationId,
                        InstallationId = "installation-1"
                    }
                },
                []);
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

    private sealed class FakeExportService : ILicenseRequestExportService
    {
        public int ExportCount { get; private set; }

        public Task<LicenseRequestExportResult> ExportAsync(
            InstallationIdentityRequest request,
            string path,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExportCount++;
            return Task.FromResult(LicenseRequestExportResult.Success(path));
        }
    }

    private sealed class FakePicker : ILicenseFilePicker
    {
        public string? LicensePath { get; set; }
        public string? RequestPath { get; set; }

        public Task<string?> PickLicensePathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LicensePath);

        public Task<string?> PickRequestExportPathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RequestPath);
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
