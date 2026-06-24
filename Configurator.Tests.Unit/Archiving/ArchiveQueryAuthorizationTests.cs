using Configurator.Application.Services.Archiving;
using Configurator.Application.Services.Authorization;
using Configurator.Application.Services.Licensing;
using Configurator.Application.Services.Modbus.Runtime;
using Xunit;

namespace Configurator.Tests.Unit.Archiving;

public sealed class ArchiveQueryAuthorizationTests
{
    [Fact]
    public async Task ArchiveQuery_DeniedAccessDoesNotInvokeInnerService()
    {
        var inner = new RecordingArchiveQueryService();
        var access = new FixedAccessDecisionService(allow: false, "LicenseMissing");
        var service = new AuthorizedArchiveQueryService(inner, access);

        var snapshots = await service.QueryRawSnapshotMetadataAsync(new ArchiveQuery());
        var commands = await service.QueryEquipmentCommandsAsync(new ArchiveQuery());
        var details = await service.GetRawSnapshotAsync(Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.False(snapshots.Succeeded);
        Assert.False(commands.Succeeded);
        Assert.False(details.Succeeded);
        Assert.Equal("PermissionDenied", snapshots.ErrorCode);
        Assert.Equal(0, inner.CallCount);
        Assert.All(access.Requirements, requirement => Assert.Equal(LicenseFeature.Archive, requirement.RequiredLicenseFeature));
    }

    [Fact]
    public async Task SecurityAuditQuery_RequiresSecurityAuditPermissionAndArchiveFeature()
    {
        var inner = new RecordingArchiveQueryService();
        var access = new FixedAccessDecisionService(allow: true);
        var service = new AuthorizedArchiveQueryService(inner, access);

        var outcome = await service.QuerySecurityAuditAsync(new ArchiveQuery());

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, inner.CallCount);
        var requirement = Assert.Single(access.Requirements);
        Assert.Equal(Permission.ViewSecurityAudit, requirement.RequiredPermission);
        Assert.Equal(LicenseFeature.Archive, requirement.RequiredLicenseFeature);
    }

    [Fact]
    public async Task ArchiveQuery_AllowedAccessInvokesInnerService()
    {
        var inner = new RecordingArchiveQueryService();
        var service = new AuthorizedArchiveQueryService(inner, new FixedAccessDecisionService(allow: true));

        var outcome = await service.QueryRuntimeEventsAsync(new ArchiveQuery());

        Assert.True(outcome.Succeeded);
        Assert.Equal(1, inner.CallCount);
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

    private sealed class RecordingArchiveQueryService : IArchiveQueryService
    {
        public int CallCount { get; private set; }

        public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveMetadataRecord>>> QueryRawSnapshotMetadataAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<RawModbusSnapshotArchiveMetadataRecord>());
        }

        public Task<ArchiveOperationResult<ArchivePage<RawModbusSnapshotArchiveRecord>>> QueryRawSnapshotsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<RawModbusSnapshotArchiveRecord>());
        }

        public Task<ArchiveOperationResult<RawModbusSnapshotArchiveRecord>> GetRawSnapshotAsync(
            Guid id,
            DateTimeOffset capturedAtUtc,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(ArchiveOperationResult<RawModbusSnapshotArchiveRecord>.Success(new RawModbusSnapshotArchiveRecord(
                id,
                "device-1",
                ModbusRuntimeRole.Client,
                1,
                capturedAtUtc,
                0,
                0,
                [],
                [],
                "hash",
                ArchiveResolution.HighResolution,
                1)));
        }

        public Task<ArchiveOperationResult<ArchivePage<ModbusStatusArchiveRecord>>> QueryModbusStatusesAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<ModbusStatusArchiveRecord>());
        }

        public Task<ArchiveOperationResult<ArchivePage<ArchiveRuntimeEventRecord>>> QueryRuntimeEventsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<ArchiveRuntimeEventRecord>());
        }

        public Task<ArchiveOperationResult<ArchivePage<EquipmentCommandAuditRecord>>> QueryEquipmentCommandsAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<EquipmentCommandAuditRecord>());
        }

        public Task<ArchiveOperationResult<ArchivePage<PhysicalModbusWriteAuditRecord>>> QueryPhysicalWritesAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<PhysicalModbusWriteAuditRecord>());
        }

        public Task<ArchiveOperationResult<ArchivePage<SecurityAuditRecord>>> QuerySecurityAuditAsync(
            ArchiveQuery query,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(SuccessPage<SecurityAuditRecord>());
        }

        private static ArchiveOperationResult<ArchivePage<T>> SuccessPage<T>()
            => ArchiveOperationResult<ArchivePage<T>>.Success(new ArchivePage<T>([], 1, 100, 0, false));
    }
}
