# PromFlow.Dispatcher — implementation progress

## Baseline

- Repository: `zdanilv/PromFlow.Dispatcher`
- Base branch: `6-add-archive`
- Baseline commit: `2afdc0de4f08bd366c44862866be6a90434f9951`
- Date: `2026-06-22 18:34:19 +03:00`
- Environment: Windows 10.0.26200, win-x64, .NET SDK `10.0.301`, MSBuild `18.6.4`
- Build status: `Passed with 10 existing warnings`
- Test status: `Passed, 283 total tests`

## Stages

- [x] Stage 0 — Baseline and documentation
- [x] Stage 1 — Archive application contracts
- [x] Stage 2 — SQLite foundation and migrations
- [x] Stage 3 — Snapshot binary codec and partitioning
- [x] Stage 4 — Buffered archive writer
- [x] Stage 5 — Modbus archive collector
- [x] Stage 6 — Command and physical write audit
- [x] Stage 7 — Query, export, retention and backup
- [x] Stage 8 — Authentication/application foundation
- [x] Stage 9 — Login, RBAC and workspace enforcement
- [x] Stage 10 — Offline license core and issuer
- [x] Stage 11 — License installation UI and feature policy
- [ ] Stage 12 — Archive UI
- [ ] Stage 13 — Centralized lifecycle
- [ ] Stage 14 — Hardening and production acceptance

## Current stage

- Stage: `11`
- Branch: `6-add-archive`
- Goal: `Add atomic license installation UI and feature enforcement`
- Status: `Completed`

## Current findings

- Initial working tree was already dirty before Stage 0 changes:
  - `AGENTS.md` modified.
  - `AgentDocs/AgentDocs.csproj` modified.
  - `AgentDocs/implementation/` untracked.
- Stage 0 changed documentation only; production code was not modified.
- Read-only context confirmed the current app shape:
  - `Configurator.Boot/Program.cs` assembles desktop DI and registers Application, Infrastructure, Modbus, OPC UA, RouteMap, workspace, dialogs and main window.
  - `Configurator.Desktop/App.axaml.cs` owns current manual shutdown path and stops Modbus runtime/facade with a 5-second timeout.
  - `WorkspaceViewModel` owns RouteMap dashboard, SignalId mapping and Modbus demo view models, and may autostart shared Modbus runtime from `ModbusDemo` options.
  - `WorkspaceView.axaml` exposes the three baseline tabs: `Route Map`, `SignalId ↔ Modbus`, `Modbus Demo`.
  - `Configurator.Infrastructure.Modbus/DependencyInjection.cs` registers one shared `IModbusRuntimeService`, a RouteMap `IModbusTcpService` facade over `Modbus.DataMap`, and a demo facade over `ModbusDemo.DataMap`.
- Baseline build warnings are existing warnings in `Configurator.Desktop`:
  - `DialogViewFactory.cs(69,13)` CS8604 nullable argument.
  - `MainWindow.axaml.cs(43,28)` CS8602 possible null dereference.
  - `MainWindow.axaml.cs(20,12)` CS8618 non-nullable field `_configService`.
  - `InputDialogViewModel.cs(36,25)` CS8619 nullable mismatch.
  - `DialogHostDialogService.cs(102,9)` CS0162 unreachable code.
  - `AuthorizationView.axaml.cs(20,13)` CS8602 possible null dereference, reported twice.
  - `AuthorizationView.axaml.cs(31,41)` CS8604 nullable owner argument.
  - `AuthorizationView.axaml(12,5)` AVLN5001 obsolete `TextBox.Watermark`.
  - `AuthorizationView.axaml(13,5)` AVLN5001 obsolete `TextBox.Watermark`.
- Build/test output also repeats the Avalonia Accelerate Community telemetry notice.
- No failing tests were observed.
- Stage 1 added application-only archive contracts in `Configurator.Application.Services.Archiving`.
- Stage 1 did not change production DI, Boot, Desktop, Infrastructure, Modbus runtime, appsettings, packages, SQLite or migrations.
- Added archive options/defaults, options validation, operation/validation results, health/envelope/query/page contracts, raw snapshot/status records, command/write/security audit records and archive service interfaces.
- Added a direct `Configurator.Tests.Unit` project reference to `Configurator.Application` so Archiving unit tests do not rely on the Desktop project transitively.
- Added `Configurator.Tests.Unit.Archiving` coverage for options validation, invalid interval/capacity/path/device ID, UTC normalization and defensive copies.
- `Configurator.Application.csproj` still contains pre-existing Avalonia package references; Stage 1 did not add or remove packages. New `Archiving` source files have no Avalonia, ReactiveUI, Desktop, Infrastructure or SQLite references.
- Stage 2 added a separate `Configurator.Infrastructure.Persistence` net10.0 project and `Configurator.Infrastructure.Persistence.Tests`.
- Added `Microsoft.Data.Sqlite` `10.0.9` only to the Persistence project, with configuration/DI/options abstraction packages pinned there.
- Added Persistence and Persistence.Tests to `DesktopTemplate.slnx`; Boot/Desktop/appsettings were not wired or changed.
- Persistence references `Configurator.Application` and does not reference Desktop, Avalonia, ReactiveUI, Modbus or OPC UA projects.
- Added `IAppDataPathProvider`, default per-user archive/export path resolution, database initialization options and persistence health state.
- Added SQLite connection factory, PRAGMA initializer, migration catalog, migration model and idempotent migration runner.
- Added migration ledger bootstrap and embedded `001_archive_foundation.sql` for archive metadata, snapshots, runtime events and indexes.
- Added file-backed Persistence tests for app-data paths, WAL/foreign keys/FULL sync/busy timeout, first/repeated/concurrent initialization, checksum mismatch, rollback, invalid paths, architecture boundaries and sensitive-material scanning.
- Stage 3 added `Configurator.Infrastructure.Persistence.Archive` with deterministic `PFS1` snapshot BLOB codec and UTC monthly partition resolver.
- Codec stores coils as 8 bool values per byte with first coil in bit 0, and holding registers as little-endian `ushort` payload via `BinaryPrimitives`.
- Codec decode returns typed `ArchiveOperationResult` failures for corrupt blobs: invalid magic, unsupported version/header/item type, invalid count, truncation and count mismatch.
- Partition resolver builds `promflow-{sanitizedDeviceId}-{yyyy-MM}.sqlite` under archive base directory, using UTC month boundaries.
- Persistence DI now registers `ArchiveSnapshotBlobCodec` and `ArchivePartitionResolver` as singleton services; Boot/Desktop/appsettings remain unchanged.
- Added Archive tests for codec roundtrip/corruption, deterministic register byte order, partition UTC boundaries/sanitization, and SQLite BLOB storage contract.
- Added architecture coverage to keep the new archive codec away from forbidden serialization/endian helpers.
- Stage 4 added a prioritized bounded single-writer archive pipeline in `Configurator.Infrastructure.Persistence.Archive`.
- Added `ArchivePriorityBuffer` with bounded wait channels for critical/normal records and latest-slot telemetry coalescing with dropped telemetry counter.
- Added `ArchiveIngestor`, `ArchiveHealthService`, `ArchiveBackoffPolicy`, stable persistence error codes and `ArchiveRuntime`.
- Added `SqliteArchiveWriter` for `RawModbusSnapshotArchiveRecord` batches into `modbus_snapshot`, using Stage 3 snapshot BLOB codec and UTC monthly partition resolver.
- Writer validates each batch before inserting, supports raw snapshot envelopes only, uses parameterized SQL and one transaction per partition group.
- Runtime flushes by batch size, timer, explicit `FlushAsync`, `StopAsync` and partition changes; Start/Stop are idempotent and Dispose is terminal.
- Persistence DI now registers `ArchiveOptionsValidator`, `IArchiveIngestor`, `IArchiveRuntime`, `IArchiveHealthService`, buffer, writer, health and backoff services as singletons.
- Boot/Desktop/appsettings, Modbus runtime and SQLite migrations remain unchanged.
- Added Stage 4 Persistence tests for priority buffering, telemetry coalescing, ingestor disabled no-op, health observation, backoff, writer insert/type failures, runtime batch/timer/flush/stop/partition lifecycle and transient write recovery.
- Added architecture coverage for Stage 4 DI registrations and for avoiding forbidden dependencies, `BinaryFormatter`, `.Wait()`, `.Result` and global channel drop policies.
- Stage 5 added `Configurator.Infrastructure.Modbus.Archiving` with `IModbusArchiveCollector`, `ModbusArchiveCollector` and deterministic `ModbusConfigurationFingerprint`.
- Collector subscribes to `IModbusRuntimeService.SnapshotChanged` and `StatusChanged` only through `StartAsync`, and unsubscribes through `StopAsync`/`DisposeAsync`; Boot/Desktop lifecycle remains unchanged.
- Snapshot callback accepts only `Client`/`Server` raw runtime snapshots, copies coils/registers, assigns monotonic sequence numbers, reads role-specific start addresses from `CurrentOptions`, computes configuration hash and enqueues high-resolution telemetry for every accepted snapshot.
- Long-term snapshot sampling is per role and respects `ArchiveOptions.LongTermSnapshotIntervalMs`.
- Status callback suppresses duplicate status tuples and enqueues meaningful transitions as `ModbusStatusArchiveRecord` with normal priority.
- Modbus DI now registers `ModbusConfigurationFingerprint`, `ModbusArchiveCollector` and `IModbusArchiveCollector` as singletons, but does not resolve or start the collector.
- `Configurator.Infrastructure.Modbus` still has no Persistence/Desktop/Avalonia/ReactiveUI dependency and does not execute SQL.
- `SqliteArchiveWriter` now also persists `ModbusStatusArchiveRecord` into existing `runtime_event` rows with parameterized SQL; no migration or schema change was added.
- Added Stage 5 tests for fingerprint stability/order sensitivity, snapshot conversion, defensive copies, role start addresses, sequence, long-term sampling, status suppression, unsubscribe/idempotency, non-blocking enqueue observation, DI registration and status runtime-event persistence.
- Stage 6 added application command-audit failure options, `CommandExecutionContext`, and a no-op `ICommandAuditService` fallback.
- `EquipmentCommandAuditRecord.WriteMode` is now nullable so missing-signal/type validation failures can be audited without inventing a physical write mode.
- `IModbusTcpService.SetAsync` now has an explicit `CommandExecutionContext?` overload; the existing overload delegates with `context: null`.
- `Configurator.Infrastructure.Persistence` now registers `CommandAuditService` and persists `EquipmentCommandAuditRecord`/`PhysicalModbusWriteAuditRecord` through `SqliteArchiveWriter`.
- Added embedded migration `002_command_write_audit.sql` for `equipment_command` and `modbus_write`, with command/search indexes and physical-write FK to command rows.
- `ModbusTcpCommandDispatcher` records requested and terminal semantic audit for validation failures, ordinary writes, pulse set/reset and cancellation. Pulse uses one `CommandId` for both physical writes.
- `ModbusTcpService` records each physical coil/register/register-array write after the low-level write attempt, including runtime role, configured physical start address, quantity, payload bytes and success/failure.
- Command audit failure policy defaults to fail-open; fail-closed blocks only ordinary commands before the first Modbus write. Emergency SignalIds remain fail-open.
- Added ADR-004 for command audit failure policy.
- `Configurator.Infrastructure.Modbus` still has no Persistence/Desktop/Avalonia/ReactiveUI/SQLite dependency and does not execute SQL.
- Stage 7 added typed Application contracts for runtime events, export requests/results, backup results and retention summaries.
- `ArchiveQuery` now supports `EventType`, and archive options now bound query page size, export range/record count, command audit retention and future security audit retention.
- `SqliteArchiveQueryService` implements paged/cancelable reads over existing monthly partitions and returns empty pages for missing optional future tables such as `security_audit`.
- Stage 7 query supports snapshots, Modbus status projections, generic runtime events, semantic equipment commands, physical Modbus writes and security audit rows when the future table exists.
- `ArchiveExportPackageWriter` writes fixed safe ZIP entries: `manifest.json`, `commands.csv`, `modbus_writes.csv`, `events.csv`, `snapshots.ndjson` and `checksums.sha256`.
- Export uses a temp staging directory and temp ZIP, then publishes by atomic move. Snapshot export writes metadata only and excludes raw snapshot BLOBs.
- `ArchiveMaintenanceService` applies retention cutoffs for high-resolution snapshots, long-term snapshots/runtime events, command/write audit and future security audit rows.
- Retention never deletes the active UTC-month partition and records an observable `ArchiveRetention` runtime event after a successful retention run.
- `ArchiveBackupPackageWriter` uses SQLite backup API per partition, verifies each backup copy with `PRAGMA quick_check`, computes SHA-256 checksums and packages verified copies into ZIP.
- Persistence DI now registers query, maintenance, export, backup, partition catalog, runtime-event writer, CSV and checksum services as singletons.
- Stage 7 did not add a migration, package, UI, auth/RBAC/license logic, Modbus runtime changes or lifecycle startup.
- Stage 8 replaced demo `IAuthApp/AuthApp` with UI-independent Application authorization contracts for roles, permissions, local users, sessions, authentication requests/results, authorization decisions, password policy and lockout policy.
- Stage 8 added `Microsoft.Extensions.Identity.Core` `10.0.6` for the `PasswordHasher<AppUser>` adapter, and aligned direct `Microsoft.Extensions.*` references to `10.0.6` to avoid NU1605 package downgrades.
- Stage 8 added a stable file-backed security SQLite database for `app_user`, with a separate security migration ledger and transactional one-time administrator bootstrap.
- Stage 8 added in-memory process session state, permission matrix services, authentication lockout/reset/signout behavior, user management foundation and best-effort sanitized security audit enqueueing.
- Stage 8 added archive migration `003_security_audit.sql`; `SqliteArchiveWriter` now persists `SecurityAuditRecord` rows and existing archive query/retention paths operate against the real table.
- Boot now references and registers Persistence DI so auth services resolve, but still does not start archive runtime/collector or enable login workspace flow.
- Desktop constructors no longer depend on the removed demo auth service; `MainViewModel` continues opening the workspace directly.
- Stage 9 added `IAccessDecisionService`, access requirements/decisions, a placeholder license feature gate, and authorized wrappers for archive maintenance, Modbus DataMap runtime and protected app config sections.
- Stage 9 routes startup to administrator bootstrap when no users exist, otherwise to login; workspace is created and initialized only after successful authentication.
- Stage 9 replaced static workspace tabs with permission-filtered descriptors for existing `Route Map`, `SignalId ↔ Modbus` and `Modbus Demo` content.
- Stage 9 tightened `UserRole.User` to `ViewRouteMap` and `IssueEquipmentCommands` only.
- Stage 9 enforces permissions at direct service boundaries for equipment commands, RouteMap configuration mutation, Modbus configuration mutation, archive maintenance/export and existing user-management operations.
- Stage 9 added unit and headless UI coverage for access decisions, denied direct service calls, login/bootstrap/logout and dynamic workspace composition.
- Stage 10 added application license contracts and deterministic offline verification for `PromFlow.License` envelopes signed with ECDSA P-256/SHA-256 over exact UTF-8 payload bytes using IeeeP1363 signatures.
- Stage 10 added typed validation statuses/errors for malformed envelopes, unknown keys, test-key rejection, signature tampering, product/version/installation mismatch, date windows, clock rollback and feature/edition inconsistency.
- Stage 10 added file-backed infrastructure for current license reads, random 256-bit installation identity and trusted time state under the per-user license directory.
- Stage 10 added `Licensing` configuration with empty production `TrustedPublicKeys`; no production public/private key material is committed.
- Stage 10 added the separate `Configurator.LicenseIssuer` CLI with `generate-key`, `issue`, `verify` and `inspect`; generated keys/licenses remain local artifacts and are ignored by git.
- Stage 10 deliberately keeps `ILicenseFeatureGate` as `NoLicenseFeatureGate`; install UI and feature enforcement remain Stage 11.
- Stage 11 added cached immutable license state via `ILicenseStateAccessor`, `ILicenseService.RefreshAsync` and atomic `ILicenseService.InstallAsync`.
- Stage 11 extended `FileLicenseStore` with `ILicenseWritableStore.ReplaceCurrentAsync`, using temp-file write/flush and atomic replace/move so invalid licenses never replace the current file.
- Stage 11 replaced the placeholder `NoLicenseFeatureGate` DI registration with `LicenseFeatureGate`; role permissions and product license features are now evaluated independently.
- Stage 11 added `EngineeringTools` and `Diagnostics` license features and bound existing workspace/service surfaces to `RouteMap`, `RemoteControl`, `Archive`, `ArchiveExport`, `EngineeringTools` and `Diagnostics`.
- Stage 11 added the Administrator License workspace tab, license file picker, installation request export and no-access workspace fallback.
- Stage 11 keeps License/User recovery paths permission-only; Administrator can install/view a license without a valid commercial license but cannot bypass missing commercial features.
- Stage 11 added best-effort sanitized security audit events for license install attempt/success/failure without storing the full license payload or signature.

## Commands last executed

```powershell
git status --short --branch
git rev-parse HEAD
dotnet --info
dotnet restore .\DesktopTemplate.slnx
dotnet build .\Configurator.Application\Configurator.Application.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Archiving"
dotnet build .\Configurator.Infrastructure.Persistence\Configurator.Infrastructure.Persistence.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~Archive"
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName=Configurator.Infrastructure.Modbus.Tests.ModbusDemoViewModelTests.StopCommandCancelsActiveLifecycleWithoutModalError"
dotnet test .\DesktopTemplate.slnx --no-restore
rg -n "password|secret|private key|BEGIN .*PRIVATE|promlicense" .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Persistence.Tests
rg -n "Avalonia|ReactiveUI|Configurator\.Desktop|Configurator\.Infrastructure\.Modbus|Configurator\.Infrastructure\.OpcUa" .\Configurator.Infrastructure.Persistence
rg -n "BinaryFormatter|\.Wait\(|\.Result|BoundedChannelFullMode\.DropOldest|BoundedChannelFullMode\.DropNewest" .\Configurator.Infrastructure.Persistence\Archive .\Configurator.Infrastructure.Persistence.Tests\Archive
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~Archiving"
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~SqliteArchiveWriter"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
rg -n "password|secret|private key|BEGIN .*PRIVATE|promlicense" .\Configurator.Infrastructure.Modbus\Archiving .\Configurator.Infrastructure.Modbus.Tests\Archiving .\Configurator.Infrastructure.Persistence\Archive .\Configurator.Infrastructure.Persistence.Tests\Archive
rg -n "Avalonia|ReactiveUI|Configurator\.Desktop|ViewModel|Configurator\.Infrastructure\.Persistence|Microsoft\.Data\.Sqlite|Sqlite|ExecuteNonQuery|ExecuteReader|Dispatcher" .\Configurator.Infrastructure.Modbus\Archiving
rg -n "\.Wait\(|\.Result|Thread\.Sleep|Task\.Delay\(" .\Configurator.Infrastructure.Modbus\Archiving
dotnet build .\Configurator.Infrastructure.Modbus\Configurator.Infrastructure.Modbus.csproj --no-restore
dotnet build .\Configurator.Infrastructure.Persistence\Configurator.Infrastructure.Persistence.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Archiving"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
rg -n "Microsoft\.Data\.Sqlite|Configurator\.Infrastructure\.Persistence|Avalonia|ReactiveUI|Configurator\.Desktop|ExecuteNonQuery|ExecuteReader" .\Configurator.Infrastructure.Modbus\Archiving .\Configurator.Infrastructure.Modbus\RouteMap .\Configurator.Infrastructure.Modbus\Runtime
rg -n "\.Wait\(|\.Result|Thread\.Sleep" .\Configurator.Infrastructure.Modbus\Archiving .\Configurator.Infrastructure.Modbus\RouteMap .\Configurator.Infrastructure.Modbus\Runtime
rg -n "password|secret|private key|BEGIN .*PRIVATE|promlicense" .\Configurator.Application\Services\Archiving .\Configurator.Infrastructure.Modbus\Archiving .\Configurator.Infrastructure.Persistence\Archive .\Configurator.Infrastructure.Persistence\Migrations
rg -n "\.Result|\.Wait\(|Thread\.Sleep" .\Configurator.Infrastructure.Persistence\Archive .\Configurator.Infrastructure.Persistence\Migrations
dotnet build .\Configurator.Application\Configurator.Application.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Archiving"
dotnet build .\Configurator.Infrastructure.Persistence\Configurator.Infrastructure.Persistence.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
rg -n "password|secret|private key|BEGIN .*PRIVATE|promlicense" .\Configurator.Application\Services\Archiving .\Configurator.Infrastructure.Persistence\Archive .\Configurator.Infrastructure.Persistence\Sqlite
rg -n "Avalonia|ReactiveUI|Configurator\.Desktop|Configurator\.Infrastructure\.Modbus|Configurator\.Infrastructure\.OpcUa" .\Configurator.Infrastructure.Persistence
rg -n "BinaryFormatter|\.Wait\(|\.Result|Thread\.Sleep|BoundedChannelFullMode\.DropOldest|BoundedChannelFullMode\.DropNewest" .\Configurator.Infrastructure.Persistence\Archive .\Configurator.Infrastructure.Persistence\Sqlite .\Configurator.Infrastructure.Persistence.Tests\Archive
dotnet restore .\DesktopTemplate.slnx
dotnet build .\Configurator.Application\Configurator.Application.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Authorization"
dotnet build .\Configurator.Infrastructure.Persistence\Configurator.Infrastructure.Persistence.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~Security"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName=Configurator.Infrastructure.Persistence.Tests.Archive.ArchiveRuntimeTests.ArchiveRuntime_TimerFlush_WritesPartialBatch|FullyQualifiedName=Configurator.Infrastructure.Persistence.Tests.Archive.ArchiveRuntimeTests.ArchiveRuntime_TransientWriteFailure_PublishesDegradedAndPersistsAfterRecovery"
dotnet test .\DesktopTemplate.slnx --no-restore
git diff --check
git status --short
git status --short
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Authorization|FullyQualifiedName~Workspace|FullyQualifiedName~RouteMapSignalRuntime"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~Security"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
rg -n "password|secret|private key|BEGIN .*PRIVATE|promlicense" .\Configurator.Application .\Configurator.Desktop .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence
rg -n "\.Wait\(|\.Result|Thread\.Sleep" .\Configurator.Application\Services .\Configurator.Desktop\Main .\Configurator.Desktop\Workspace
git diff --check
git status --short
```

## Test results

- Restore: `Passed; all projects up-to-date`
- Application build: `Passed; 0 warnings, 0 errors`
- Persistence build: `Passed; 1 NU1903 warning from transitive SQLitePCLRaw.lib.e_sqlite3`
- Build: `Passed; 2 NU1903 warnings, 0 errors in the final Stage 6 run`
- Archiving unit tests: `Passed; 20 passed, 0 failed, 0 skipped`
- Modbus archive-focused tests: `Passed; 20 passed, 0 failed, 0 skipped`
- Archive-focused Persistence writer tests: `Passed; 5 passed, 0 failed, 0 skipped`
- Archive-focused Persistence tests: `Passed; 44 passed, 0 failed, 0 skipped`
- Persistence tests: `Passed; 59 passed, 0 failed, 0 skipped`
- Full tests: `Passed; 397 passed, 0 failed, 0 skipped`
- Unit tests: `Passed; Configurator.Tests.Unit, 161 passed`
- Modbus tests: `Passed; Configurator.Infrastructure.Modbus.Tests, 111 passed`
- OPC UA tests: `Passed as part of full solution test; Configurator.Infrastructure.OpcUa.Tests, 35 passed`
- RouteMap UI tests: `Passed as part of full solution test; Configurator.Tests.RouteMap.Ui, 27 passed`
- Sensitive-material scan over new Archiving source and test files: `Passed`
- Sensitive-material scan over new Persistence source and test files: `Passed`
- Forbidden-dependency scan over new Persistence source files: `Passed`
- BinaryFormatter/wait/result/global-drop scan over archive persistence/test files: `Passed`
- Sensitive-material scan over Modbus archiving, Modbus archiving tests, Persistence archive and Persistence archive tests: `Passed`
- Forbidden-dependency scan over Modbus archiving source files: `Passed`
- Blocking-call scan over Modbus archiving source files: `Passed`
- Application Archiving unit tests after Stage 6: `Passed; 25 passed, 0 failed, 0 skipped`
- Persistence tests after Stage 6 migration/storage: `Passed; 63 passed, 0 failed, 0 skipped`
- Modbus tests after Stage 6 command/physical audit: `Passed; 111 passed, 0 failed, 0 skipped`
- RouteMap unit filter after Stage 6 dispatcher changes: `Passed; 136 passed, 0 failed, 0 skipped`
- Stage 6 forbidden dependency, sensitive-material and blocking-call scans: `Passed`
- Application Archiving unit tests after Stage 7 contracts/options: `Passed; 33 passed, 0 failed, 0 skipped`
- Persistence tests after Stage 7 query/export/retention/backup: `Passed; 71 passed, 0 failed, 0 skipped`
- Full build after Stage 7: `Passed; 2 NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3`
- Full tests after Stage 7: `Passed; 413 passed, 0 failed, 0 skipped`
- Stage 7 sensitive-material, forbidden-dependency and blocking-call scans: `Passed`
- Stage 7 diff whitespace check: `Passed`
- Restore after Stage 8 package addition/alignment: `Passed; NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3`
- Application build after Stage 8 contracts: `Passed; 0 errors`
- Authorization unit tests after Stage 8: `Passed; 4 passed, 0 failed, 0 skipped`
- Persistence build after Stage 8 security services: `Passed; 1 NU1903 warning from SQLitePCLRaw.lib.e_sqlite3`
- Security-focused Persistence tests after Stage 8: `Passed; 13 passed, 0 failed, 0 skipped`
- Persistence tests after Stage 8 migrations/storage: `Passed; 83 passed, 0 failed, 0 skipped`
- Full build after Stage 8: `Passed; 3 NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3`
- Full tests after Stage 8: `Passed on rerun; 429 passed, 0 failed, 0 skipped`
- First full Stage 8 test run had transient SQLite failures in two existing `ArchiveRuntimeTests`; targeted rerun passed `2 passed`, then final full rerun passed.
- Full build after Stage 9: `Passed; 8 warnings total: 3 NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3 and 5 existing Desktop nullable/unreachable-code warnings`
- Stage 9 targeted unit tests: `Passed; 47 passed, 0 failed, 0 skipped`
- Stage 9 security-focused Persistence tests: `Passed; 14 passed, 0 failed, 0 skipped`
- Persistence tests after Stage 9: `Passed on rerun; 84 passed, 0 failed, 0 skipped`
- First full Persistence run after Stage 9 had a transient existing archive retention SQLite disposed-object failure; immediate rerun passed.
- Modbus tests after Stage 9: `Passed; 111 passed, 0 failed, 0 skipped`
- RouteMap unit filter after Stage 9: `Passed; 137 passed, 0 failed, 0 skipped`
- RouteMap headless UI tests after Stage 9: `Passed; 29 passed, 0 failed, 0 skipped`
- Full tests after Stage 9: `Passed; 451 passed, 0 failed, 0 skipped`
- Stage 9 sensitive-material scan found only expected auth/password identifiers and no hardcoded production secrets, private keys or license material.
- Stage 9 blocking-call scan found one false positive on `RouteMapSettingsViewModel.Result` property access and no `.Wait()`, task `.Result` or `Thread.Sleep`.
- Stage 9 diff whitespace check: `Passed; only CRLF normalization warnings`
- Stage 10 issuer build with restore/assets generation: `Passed; 0 warnings, 0 errors`
- Full build after Stage 10: `Passed; 3 NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3, 0 errors`
- Stage 10 licensing unit/integration tests: `Passed; 23 passed, 0 failed, 0 skipped`
- Stage 9 auth/workspace regression after Stage 10: `Passed; 23 passed, 0 failed, 0 skipped`
- Workspace authorization headless UI regression after Stage 10: `Passed; 2 passed, 0 failed, 0 skipped`
- Stage 10 issuer CLI manual flow: `Passed; help, generate-key, issue, verify and inspect succeeded with generated temp artifacts removed`
- Full tests after Stage 10: `Passed; 474 passed, 0 failed, 0 skipped`
- Stage 10 private-key/blocking-call scan over new license/issuer paths: `Passed; no matches`
- Full build after Stage 11: `Passed; 3 NU1903 warnings from SQLitePCLRaw.lib.e_sqlite3, 0 errors`
- Stage 11 licensing/auth/workspace unit tests: `Passed; 61 passed, 0 failed, 0 skipped`
- Stage 11 licensing unit tests after final install-byte copy cleanup: `Passed; 36 passed, 0 failed, 0 skipped`
- Stage 11 RouteMap unit regression: `Passed; 138 passed, 0 failed, 0 skipped`
- Stage 11 Modbus regression: `Passed; 111 passed, 0 failed, 0 skipped`
- Stage 11 security-focused Persistence tests: `Passed; 14 passed, 0 failed, 0 skipped`
- Stage 11 headless UI tests: `Passed; 30 passed, 0 failed, 0 skipped`
- Full tests after Stage 11: `Passed on rerun; 490 passed, 0 failed, 0 skipped`
- First full Stage 11 run had a transient existing `ArchiveRuntime_PartitionChange_WritesSeparateMonthlyDatabases` SQLite prepare failure; the targeted rerun passed, then the final full rerun passed.
- Stage 11 private-key and blocking-call scans over new license paths: `Passed; no matches`

## Known limitations

- Stage 0 did not fix existing compiler/Avalonia warnings.
- Stage 1 did not implement archive storage, buffering, runtime collection, query execution, export, retention, authorization, licensing or UI features.
- Stage 2 did not implement archive buffering, Modbus collection, binary snapshot codec, partition resolver, query execution, export, retention, backup, authorization, licensing or UI features.
- Stage 2 did not wire Persistence into Boot/Desktop runtime.
- Stage 3 did not implement archive buffering, writer connection rotation, Modbus collection, query execution, export, retention, backup, authorization, licensing or UI features.
- Stage 3 did not change SQLite schema or create a new migration because Stage 2 `modbus_snapshot` already stores snapshot BLOB columns and counts.
- Stage 4 did not implement Modbus collection, query execution, export, retention, backup, command/security audit persistence, authorization, licensing or UI features.
- Stage 4 did not wire Persistence into Boot/Desktop runtime and did not change `appsettings.json`.
- Stage 4 did not add a migration because Stage 2 `modbus_snapshot` already stores raw snapshot rows and Stage 3 defines the BLOB format.
- Stage 5 did not wire or start archive runtime/collector from Boot/Desktop; centralized lifecycle remains Stage 13.
- Stage 5 did not implement query execution, export, retention, backup, command/physical write audit, security audit, authorization, licensing or UI features.
- Stage 5 did not add a migration because Modbus status transitions use the existing Stage 2 `runtime_event` table.
- Stage 5 writer persists `RawModbusSnapshotArchiveRecord` and `ModbusStatusArchiveRecord`; command/security audit persistence remains for later stages.
- NuGet restore/build reports NU1903 for transitive `SQLitePCLRaw.lib.e_sqlite3` `2.1.11` through the plan-pinned `Microsoft.Data.Sqlite` `10.0.9`.
- One full-suite run had a transient failure in existing `ModbusDemoViewModelTests.StopCommandCancelsActiveLifecycleWithoutModalError`; the targeted rerun and the final full rerun passed.
- Stage 6 did not wire or start `IArchiveRuntime`/`IModbusArchiveCollector` from Boot/Desktop; centralized lifecycle remains Stage 13.
- Stage 6 did not implement query execution, export, retention, backup, Archive UI, authentication, RBAC, licensing or security audit persistence.
- Stage 6 leaves `SessionId`, `UserId` and `Username` nullable until real auth stages.
- Physical write audit is attached only to writes that pass through `IModbusTcpService.SetAsync` with a non-null `CommandExecutionContext`; direct low-level client/server writes remain out of scope.
- Stage 7 did not add Archive UI, authentication/RBAC/license enforcement, security audit schema creation or centralized lifecycle startup.
- Stage 7 backup/export APIs create operational ZIP artifacts only when called explicitly; Boot/Desktop still do not start archive runtime or collector until Stage 13.
- Stage 7 treats missing `security_audit` as empty because the table is owned by a later stage.
- Stage 8 did not enable login navigation, dynamic workspace/tab visibility, command enforcement, license checks, Archive UI or centralized lifecycle startup; those remain later stages.
- Stage 8 security audit enqueueing is best-effort and depends on archive runtime startup in later lifecycle stages for background flushing.
- Stage 8 stores users in a stable security database while security audit rows live in monthly archive partitions.
- Stage 9 reserves `RequiredLicenseFeature` in access and tab descriptors, but all current descriptors use `null`; real license validation remains Stage 10/11.
- Stage 9 does not add Archive/User/License workspace tabs because those UI surfaces belong to later stages.
- Stage 9 does not start archive runtime/collector or centralized lifecycle; Stage 13 still owns that lifecycle work.
- Stage 10 does not install or replace the current license file; atomic install and administrator UI remain Stage 11.
- Stage 10 does not assign non-null license features to workspace descriptors or service guards; Stage 11 owns feature policy enforcement.
- Stage 10 ships with an empty production trusted key ring in `appsettings.json`; deployment must supply trusted public keys before customer license validation can succeed.
- Stage 10 clock rollback protection is local offline best-effort state, not a tamper-proof online time authority.
- Stage 11 does not add Archive UI, User Management UI, online activation, migrations, packages or production key material.
- Stage 11 still ships with an empty production trusted key ring; customer licenses require deployment-supplied trusted public keys.
- Stage 11 request export writes local `.promrequest` artifacts only when invoked explicitly by an administrator.

## Next action

Stop here until Stage 12 is explicitly requested.
