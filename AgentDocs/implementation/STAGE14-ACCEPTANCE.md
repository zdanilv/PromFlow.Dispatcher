# Stage 14 Acceptance Evidence

Stage 14 goal: close production acceptance without adding product features. The stage
hardens archive/auth/license/lifecycle/command paths, closes disabled active-session
authorization, adds automated source/documentation checks and updates EN/RU operator
guides.

## Scope Confirmation

- Product features: none added.
- UI features: none added.
- NuGet packages: none added.
- SQL migrations: none added.
- Production keys or license artifacts: none added.
- Stage 15: not started.

## Automated Failure And Load Scenarios

| Scenario | Evidence source | Expected result |
|---|---|---|
| disabled current user | `SessionRevocationAcceptanceTests.DisableCurrentUser_ClearsSessionAndNextManageUsersCallIsNotAuthenticated` | active session cleared; next protected call is `NotAuthenticated` |
| disabled different user | `SessionRevocationAcceptanceTests.DisableDifferentUser_KeepsCurrentAdminSession` | administrator session remains authenticated |
| license expiry during session | `LicenseSessionExpiryAcceptanceTests.ExpiredLicenseState_DeniesNextLicensedAccessEvenForAdministrator` | next licensed access is denied with expired license state |
| archive load matrix | `ArchiveProductionAcceptanceTests.LoadMatrix_PersistsBoundedPagesAndLeavesQueueEmpty` | accelerated captures persist and first bounded page reads without full-history query |
| archive queue pressure | `ArchiveProductionAcceptanceTests.Burst10x_CoalescesTelemetryAndPreservesCriticalAndNormalRecords` | critical/normal records are preserved and telemetry drop count grows |
| failed archive transaction | `ArchiveProductionAcceptanceTests.FailedBatch_DoesNotCommitPartialRows` | duplicate snapshot batch rolls back without partial rows |
| corrupted archive partition | `ArchiveProductionAcceptanceTests.CorruptedOldPartition_ReturnsTypedFailureAndLaterPartitionStillQueries` | corrupt old row returns typed failure and later partition still queries |
| large export limit | `ArchiveProductionAcceptanceTests.LargeExportLimitFailure_RemovesTemporaryExportDirectory` | export limit fails cleanly and temp package directory is removed |
| connection loss during command | `ModbusCommandAuditTests.Dispatcher_ConnectionLossDuringCommand_AuditsFailedConnectionLost` | terminal failed command audit is recorded |
| pulse shutdown interruption | `ModbusCommandAuditTests.Dispatcher_ShutdownGateRejectsPulseBeforePhysicalWrite` | pulse is rejected before physical write and terminal audit is recorded |
| source scan | `ProductionHardeningSourceScanTests` plus `rg` commands | no blocking calls, private keys, debug bypasses or generated license artifacts |
| documentation coverage | `ProductionDocumentationTests` | EN/RU guides 07-10 exist and master docs link them |
| operations runbook | `AgentDocs/en/10-operations-and-recovery.en.md`, `AgentDocs/ru/10-operations-and-recovery.ru.md` | manual 24-hour endurance runbook is documented without CI wall-clock waits |
| full verification | `dotnet build`, focused tests, full solution tests, scans and `git diff --check` | production hardening evidence is reproducible |

## Metrics Evidence Sources

- Archive health: `ArchiveHealth.State`, `QueueDepth`, `DroppedTelemetryCount`,
  `LastSuccessAtUtc`, `LastErrorCode`, `LastErrorMessage`.
- Archive queries: paged query result counts and typed failure codes.
- Security audit: session disable, license install and protected-action events.
- License state: `LicenseState.Status`, dates, feature list and reason code.
- Command audit: equipment command terminal status and physical write audit rows.
- Lifecycle: `ApplicationRuntimeCoordinator` startup results and shutdown coordinator
  results.

## Verification Commands

The Stage 14 verification set is:

```powershell
git status --short --branch
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Hardening|FullyQualifiedName~Documentation|FullyQualifiedName~LicenseSessionExpiry|FullyQualifiedName~Licensing|FullyQualifiedName~Authorization|FullyQualifiedName~Workspace|FullyQualifiedName~Runtime"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~ArchiveProductionAcceptance|FullyQualifiedName~SessionRevocation|FullyQualifiedName~Archive|FullyQualifiedName~Security"
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~ModbusCommandAudit|FullyQualifiedName~ModbusArchive|FullyQualifiedName~Runtime"
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter "FullyQualifiedName~Archive|FullyQualifiedName~License|FullyQualifiedName~WorkspaceAuthorization"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
rg -n "BinaryFormatter|\.Wait\(|\.Result|Thread\.Sleep" .\Configurator.Application .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Modbus .\Configurator.Desktop\Main .\Configurator.Desktop\Runtime .\Configurator.Desktop\Workspace
rg -n "BEGIN .*PRIVATE|PRIVATE KEY-----|AllowTestKeys\s*=\s*true|debug bypass|hardcoded password" .\Configurator.Application .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Modbus .\Configurator.Desktop .\Configurator.Boot
Get-ChildItem -Path . -Recurse -File -Include *.pem,*.promlicense,*.promrequest | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }
git diff --check
git status --short --branch
```

## Current Results

- Build: `Passed; 3 NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3, 0 errors`.
- Stage 14 Unit hardening/documentation/license/auth/workspace/runtime focused tests: `Passed; 124 passed, 0 failed, 0 skipped`.
- Stage 14 Persistence archive/session/security focused tests: `Passed; 80 passed, 0 failed, 0 skipped`.
- Stage 14 Modbus command-audit/archive/runtime focused tests: `Passed; 45 passed, 0 failed, 0 skipped`.
- Stage 14 headless Archive/License/Workspace UI smoke tests: `Passed; 4 passed, 0 failed, 0 skipped`.
- Full Persistence tests: `Passed; 93 passed, 0 failed, 0 skipped`.
- Full Modbus tests: `Passed; 115 passed, 0 failed, 0 skipped`.
- Full solution tests: `Passed; 528 passed, 0 failed, 0 skipped`.
- Blocking-call scan: `Passed; no matches`.
- Private-key/debug-bypass scan: `Passed; no matches`.
- Generated license/private key artifact scan: `Passed; no files`.
- Diff whitespace check: `Passed; only CRLF normalization warnings reported by git`.

## Known Limitations

- Manual 24-hour endurance is documented as an operator runbook, not a CI wall-clock
  requirement.
- External database edits that disable a user outside this process are an operational
  recovery risk; in-process `SetUserEnabledAsync` is enforced.
- Offline clock rollback detection is local best effort.
- Production `TrustedPublicKeys` remains deployment-supplied; no production key material
  is committed.
- Existing transitive `NU1903` warnings for `SQLitePCLRaw.lib.e_sqlite3` remain until a
  separate dependency-policy stage changes package versions.
