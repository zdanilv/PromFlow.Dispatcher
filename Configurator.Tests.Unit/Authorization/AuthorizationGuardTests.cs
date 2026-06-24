using Configurator.Application.Services;
using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Configuration;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Configuration;
using Configurator.Application.Services.Modbus.Contracts;
using Configurator.Application.Services.Modbus.Runtime;
using Configurator.Application.Services.Signals;
using Configurator.Desktop.Workspace.RouteMap.Services;
using Xunit;

namespace Configurator.Tests.Unit.Authorization;

public sealed class AuthorizationGuardTests
{
    [Fact]
    public async Task EquipmentCommand_DeniedAccessDoesNotInvokeActiveDispatcher()
    {
        var dispatcher = new RecordingCommandDispatcher();
        var access = new FixedAccessDecisionService(allow: false, "NotAuthenticated");
        using var runtime = new RouteMapSignalRuntime(
            new EmptySignalProvider(),
            dispatcher,
            new EmptySignalProvider(),
            new RecordingCommandDispatcher(),
            access,
            RouteMapSignalSource.Mock);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            runtime.DispatchAsync(new SignalWriteRequest("system.emergency", true, SignalValueType.Bool)));

        Assert.Empty(dispatcher.Requests);
        Assert.Equal(LicenseFeature.RemoteControl, access.Requirements.Single().RequiredLicenseFeature);
    }

    [Fact]
    public async Task EquipmentCommand_AllowedAccessInvokesActiveDispatcher()
    {
        var dispatcher = new RecordingCommandDispatcher();
        using var runtime = new RouteMapSignalRuntime(
            new EmptySignalProvider(),
            dispatcher,
            new EmptySignalProvider(),
            new RecordingCommandDispatcher(),
            new FixedAccessDecisionService(allow: true),
            RouteMapSignalSource.Mock);

        await runtime.DispatchAsync(new SignalWriteRequest("system.emergency", true, SignalValueType.Bool));

        Assert.Single(dispatcher.Requests);
    }

    [Fact]
    public void ModbusDataMap_DeniedAccessDoesNotInvokeInnerRuntime()
    {
        var inner = new RecordingDataMapRuntime();
        var access = new FixedAccessDecisionService(allow: false, "PermissionDenied");
        var runtime = new AuthorizedModbusDataMapRuntime(
            inner,
            access);

        var result = runtime.ApplyDataMap([new ModbusDataPointOptions { Name = "signal" }]);

        Assert.False(result.Succeeded);
        Assert.Equal("PermissionDenied", result.ErrorCode);
        Assert.Equal(0, inner.ApplyCount);
        Assert.Equal(LicenseFeature.EngineeringTools, access.Requirements.Single().RequiredLicenseFeature);
    }

    [Fact]
    public void ModbusDataMap_AllowedAccessInvokesInnerRuntime()
    {
        var inner = new RecordingDataMapRuntime();
        var runtime = new AuthorizedModbusDataMapRuntime(
            inner,
            new FixedAccessDecisionService(allow: true));

        var result = runtime.ApplyDataMap([new ModbusDataPointOptions { Name = "signal" }]);

        Assert.True(result.Succeeded);
        Assert.Equal(1, inner.ApplyCount);
    }

    [Fact]
    public async Task ArchiveMaintenance_DeniedAccessDoesNotInvokeInnerService()
    {
        var inner = new RecordingArchiveMaintenanceService();
        var access = new FixedAccessDecisionService(allow: false, "NotAuthenticated");
        var service = new AuthorizedArchiveMaintenanceService(
            inner,
            access);

        var retention = await service.ApplyRetentionAsync(DateTimeOffset.UtcNow);
        var export = await service.ExportAsync(new ArchiveExportRequest(new ArchiveQuery()));
        var backup = await service.CreateBackupAsync("out");

        Assert.False(retention.Succeeded);
        Assert.False(export.Succeeded);
        Assert.False(backup.Succeeded);
        Assert.Equal(0, inner.ApplyRetentionCount);
        Assert.Equal(0, inner.ExportCount);
        Assert.Equal(0, inner.BackupCount);
        Assert.Equal(
            [LicenseFeature.Archive, LicenseFeature.ArchiveExport, LicenseFeature.Archive],
            access.Requirements.Select(requirement => requirement.RequiredLicenseFeature!).ToArray());
    }

    [Fact]
    public async Task ArchiveMaintenance_AllowedAccessInvokesInnerService()
    {
        var inner = new RecordingArchiveMaintenanceService();
        var service = new AuthorizedArchiveMaintenanceService(
            inner,
            new FixedAccessDecisionService(allow: true));

        var retention = await service.ApplyRetentionAsync(DateTimeOffset.UtcNow);
        var export = await service.ExportAsync(new ArchiveExportRequest(new ArchiveQuery()));
        var backup = await service.CreateBackupAsync("out");

        Assert.True(retention.Succeeded);
        Assert.True(export.Succeeded);
        Assert.True(backup.Succeeded);
        Assert.Equal(1, inner.ApplyRetentionCount);
        Assert.Equal(1, inner.ExportCount);
        Assert.Equal(1, inner.BackupCount);
    }

    [Fact]
    public async Task AppConfig_ProtectedSectionsRequireConfigureModbus()
    {
        var inner = new RecordingAppConfigService();
        var denied = new AuthorizedAppConfigService(
            inner,
            new FixedAccessDecisionService(allow: false, "PermissionDenied"));
        var allowed = new AuthorizedAppConfigService(
            inner,
            new FixedAccessDecisionService(allow: true));

        await denied.SaveSectionAsync("Unprotected", new object());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            denied.SaveSectionAsync(ModbusOptions.SectionName, new ModbusOptions()));
        await allowed.SaveSectionAsync(ModbusOptions.SectionName, new ModbusOptions());

        Assert.Equal(2, inner.SaveCount);
        Assert.Contains("Unprotected", inner.SavedSections);
        Assert.Contains(ModbusOptions.SectionName, inner.SavedSections);
    }

    private sealed class FixedAccessDecisionService(bool allow, string reasonCode = "PermissionDenied")
        : IAccessDecisionService
    {
        private static readonly UserSession Session = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "admin",
            UserRole.Administrator,
            Enum.GetValues<Permission>(),
            DateTimeOffset.UtcNow);

        public List<AccessRequirement> Requirements { get; } = [];

        public AccessDecision Authorize(AccessRequirement requirement)
        {
            Requirements.Add(requirement);
            return allow
                ? AccessDecision.Allow(requirement, Session)
                : AccessDecision.Deny(requirement, reasonCode, Session);
        }

        public Task<AccessDecision> AuthorizeAsync(
            AccessRequirement requirement,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Authorize(requirement));
        }
    }

    private sealed class EmptySignalProvider : ISignalValueProvider
    {
        public IObservable<IReadOnlyDictionary<string, SignalValue>> Observe() =>
            System.Reactive.Linq.Observable.Empty<IReadOnlyDictionary<string, SignalValue>>();
    }

    private sealed class RecordingCommandDispatcher : IEquipmentCommandDispatcher
    {
        public List<SignalWriteRequest> Requests { get; } = [];

        public Task DispatchAsync(
            SignalWriteRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingDataMapRuntime : IModbusDataMapRuntime
    {
        public int ApplyCount { get; private set; }

        public ModbusOperationResult ApplyDataMap(IReadOnlyList<ModbusDataPointOptions> dataMap)
        {
            ApplyCount++;
            return ModbusOperationResult.Success();
        }
    }

    private sealed class RecordingArchiveMaintenanceService : IArchiveMaintenanceService
    {
        public int ApplyRetentionCount { get; private set; }
        public int ExportCount { get; private set; }
        public int BackupCount { get; private set; }

        public Task<ArchiveOperationResult<ArchiveRetentionResult>> ApplyRetentionAsync(
            DateTimeOffset nowUtc,
            CancellationToken cancellationToken = default)
        {
            ApplyRetentionCount++;
            return Task.FromResult(ArchiveOperationResult<ArchiveRetentionResult>.Success(
                new ArchiveRetentionResult(nowUtc.ToUniversalTime(), [])));
        }

        public Task<ArchiveOperationResult<ArchiveExportResult>> ExportAsync(
            ArchiveExportRequest request,
            CancellationToken cancellationToken = default)
        {
            ExportCount++;
            return Task.FromResult(ArchiveOperationResult<ArchiveExportResult>.Success(
                new ArchiveExportResult("export.zip", DateTimeOffset.UtcNow, 0, 0, 0, 0, new Dictionary<string, string>())));
        }

        public Task<ArchiveOperationResult<ArchiveBackupResult>> CreateBackupAsync(
            string destinationDirectory,
            CancellationToken cancellationToken = default)
        {
            BackupCount++;
            return Task.FromResult(ArchiveOperationResult<ArchiveBackupResult>.Success(
                new ArchiveBackupResult("backup.zip", DateTimeOffset.UtcNow, [], new Dictionary<string, string>())));
        }
    }

    private sealed class RecordingAppConfigService : IAppConfigService
    {
        public int SaveCount { get; private set; }
        public List<string> SavedSections { get; } = [];

        public T GetSection<T>(string sectionName) where T : class, new() => new();

        public string GetValue(string key) => string.Empty;

        public Task SaveSectionAsync<T>(string sectionName, T value, CancellationToken ct = default)
        {
            SaveCount++;
            SavedSections.Add(sectionName);
            return Task.CompletedTask;
        }

        public void SaveUserSettings(UserSettings settings) { }

        public UserSettings LoadUserSettings() => new();
    }
}
