using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Desktop.Workspace;
using ReactiveUI;
using Xunit;

namespace Configurator.Tests.RouteMap.Ui;

public sealed class WorkspaceAuthorizationVisualTests
{
    [AvaloniaFact]
    public async Task UserWorkspaceRendersRouteMapTab()
    {
        using var fixture = await WorkspaceFixture.CreateAsync(
            Permission.ViewRouteMap,
            Permission.IssueEquipmentCommands);

        Assert.Equal(["Route Map"], fixture.TabHeaders());
        Assert.Equal(1, fixture.CreatedContentCount);
        Assert.DoesNotContain(fixture.VisibleText(), text => text == "PromFlow Dispatcher");
    }

    [AvaloniaFact]
    public async Task AdminWorkspaceRendersAuthorizedTabsWithoutModbusTcp()
    {
        using var fixture = await WorkspaceFixture.CreateAsync(Enum.GetValues<Permission>());

        Assert.Equal(
            ["Route Map", "SignalId ↔ Modbus", "Modbus Demo", "Archive", "License", "Users"],
            fixture.TabHeaders());
        Assert.Equal(1, fixture.CreatedContentCount);
        Assert.DoesNotContain(fixture.VisibleText(), text => text == "PromFlow Dispatcher");
        Assert.DoesNotContain(fixture.VisibleText(), text => text == "Modbus TCP");
    }

    [AvaloniaFact]
    public async Task AdminWorkspaceCreatesVisibleContentWhenTabsAreSelected()
    {
        using var fixture = await WorkspaceFixture.CreateAsync(Enum.GetValues<Permission>());

        fixture.SelectTab("Archive");
        fixture.SelectTab("Users");
        fixture.SelectTab("Archive");

        Assert.Equal(3, fixture.CreatedContentCount);
        Assert.Contains("Content: Archive", fixture.VisibleText());
    }

    private sealed class WorkspaceFixture : IDisposable
    {
        private WorkspaceFixture(
            Window window,
            WorkspaceView view,
            WorkspaceViewModel viewModel,
            FactoryCounters counters)
        {
            Window = window;
            View = view;
            ViewModel = viewModel;
            Counters = counters;
        }

        private Window Window { get; }
        private WorkspaceView View { get; }
        public WorkspaceViewModel ViewModel { get; }
        private FactoryCounters Counters { get; }
        public int CreatedContentCount => Counters.CreatedContentCount;

        public static async Task<WorkspaceFixture> CreateAsync(params Permission[] permissions)
        {
            var sessionAccessor = new TestSessionAccessor();
            sessionAccessor.SetCurrent(new UserSession(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "operator",
                UserRole.User,
                permissions,
                DateTimeOffset.UtcNow));
            var counters = new FactoryCounters();
            var viewModel = new WorkspaceViewModel(
                new TestScreen(),
                new EmptyServiceProvider(),
                sessionAccessor,
                CreateDescriptors(counters),
                new DefaultAccessDecisionService(sessionAccessor, new LicenseFeatureGate(new ValidLicenseStateAccessor())),
                new FakeAuthenticationService(),
                new FakeLicenseService(),
                (_, _) => Task.CompletedTask);
            await viewModel.InitializeAsync();

            var view = new WorkspaceView { DataContext = viewModel };
            var window = new Window { Width = 1200, Height = 760, Content = view };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            return new WorkspaceFixture(window, view, viewModel, counters);
        }

        public string[] TabHeaders()
            => ViewModel.Tabs.Select(tab => tab.Header).ToArray();

        public void SelectTab(string header)
        {
            var tabControl = View.GetVisualDescendants().OfType<TabControl>().Single();
            tabControl.SelectedIndex = Array.IndexOf(TabHeaders(), header);
            Dispatcher.UIThread.RunJobs();
        }

        public string[] VisibleText()
            => View.GetVisualDescendants()
                .OfType<TextBlock>()
                .Select(text => text.Text)
                .Where(text => text is not null)
                .Cast<string>()
                .ToArray();

        public void Dispose()
        {
            Window.Close();
            ViewModel.Dispose();
        }

        private static IReadOnlyList<WorkspaceTabDescriptor> CreateDescriptors(FactoryCounters counters) =>
        [
            new("route-map", "Route Map", Permission.ViewRouteMap, LicenseFeature.RouteMap, _ => counters.CreateContent("Route Map"), 0),
            new("signal-map", "SignalId ↔ Modbus", Permission.ViewSignalMapping, LicenseFeature.EngineeringTools, _ => counters.CreateContent("SignalId ↔ Modbus"), 10),
            new("modbus-demo", "Modbus Demo", Permission.ViewModbusDiagnostics, LicenseFeature.Diagnostics, _ => counters.CreateContent("Modbus Demo"), 20),
            new("archive", "Archive", Permission.ViewArchive, LicenseFeature.Archive, _ => counters.CreateContent("Archive"), 25),
            new("license", "License", Permission.ViewLicense, null, _ => counters.CreateContent("License"), 30),
            new("users", "Users", Permission.ManageUsers, null, _ => counters.CreateContent("Users"), 40),
        ];

        private sealed class FactoryCounters
        {
            public int CreatedContentCount { get; private set; }

            public object CreateContent(string header)
            {
                CreatedContentCount++;
                return new TextBlock { Text = $"Content: {header}" };
            }
        }
    }

    private sealed class ValidLicenseStateAccessor : ILicenseStateAccessor
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
                Features =
                [
                    LicenseFeature.RouteMap,
                    LicenseFeature.RemoteControl,
                    LicenseFeature.EngineeringTools,
                    LicenseFeature.Diagnostics,
                    LicenseFeature.Archive,
                    LicenseFeature.ArchiveExport,
                ],
                Installation = new LicenseInstallationProfile
                {
                    BindingMode = LicenseInstallationBindingMode.InstallationId,
                    InstallationId = "installation-1"
                }
            },
            []);

        public event EventHandler<LicenseStateChangedEventArgs>? StateChanged { add { } remove { } }
    }

    private sealed class FakeLicenseService : ILicenseService
    {
        public Task<LicenseState> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            RefreshAsync(cancellationToken);

        public Task<LicenseState> RefreshAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ValidLicenseStateAccessor().Current);
        }

        public Task<LicenseInstallResult> InstallAsync(
            LicenseInstallRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LicenseValidationResult> VerifyAsync(
            byte[] licenseBytes,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestScreen : IScreen
    {
        public RoutingState Router { get; } = new();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private sealed class TestSessionAccessor : IUserSessionAccessor
    {
        public UserSessionSnapshot Current { get; private set; } = UserSessionSnapshot.Anonymous;
        public void SetCurrent(UserSession session) => Current = UserSessionSnapshot.Authenticated(session);
        public void Clear() => Current = UserSessionSnapshot.Anonymous;
        public bool ClearIfCurrent(Guid userId)
        {
            if (userId == Guid.Empty)
            {
                throw new ArgumentException("User id must not be empty.", nameof(userId));
            }

            if (Current.Session?.UserId != userId)
            {
                return false;
            }

            Current = UserSessionSnapshot.Anonymous;
            return true;
        }
    }

    private sealed class FakeAuthenticationService : IAuthenticationService
    {
        public Task<AuthenticationResult> AuthenticateAsync(
            AuthenticationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthenticationResult.InvalidCredentials());

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
