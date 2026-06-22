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
- [ ] Stage 2 — SQLite foundation and migrations
- [ ] Stage 3 — Snapshot binary codec and partitioning
- [ ] Stage 4 — Buffered archive writer
- [ ] Stage 5 — Modbus archive collector
- [ ] Stage 6 — Command and physical write audit
- [ ] Stage 7 — Query, export, retention and backup
- [ ] Stage 8 — Authentication/application foundation
- [ ] Stage 9 — Login, RBAC and workspace enforcement
- [ ] Stage 10 — Offline license core and issuer
- [ ] Stage 11 — License installation UI and feature policy
- [ ] Stage 12 — Archive UI
- [ ] Stage 13 — Centralized lifecycle
- [ ] Stage 14 — Hardening and production acceptance

## Current stage

- Stage: `1`
- Branch: `6-add-archive`
- Goal: `Add archive application contracts without SQLite, runtime wiring, DI, UI or migrations`
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
- Stage 2 has not been started.

## Commands last executed

```powershell
git status --short --branch
git rev-parse HEAD
dotnet --info
dotnet restore .\DesktopTemplate.slnx
dotnet build .\Configurator.Application\Configurator.Application.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Archiving"
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

## Test results

- Restore: `Passed; all projects up-to-date`
- Application build: `Passed; 0 warnings, 0 errors`
- Build: `Passed; 0 warnings, 0 errors in the final Stage 1 run`
- Archiving unit tests: `Passed; 20 passed, 0 failed, 0 skipped`
- Full tests: `Passed; 303 passed, 0 failed, 0 skipped`
- Unit tests: `Passed; Configurator.Tests.Unit, 156 passed`
- Modbus tests: `Passed as part of full solution test; Configurator.Infrastructure.Modbus.Tests, 85 passed`
- OPC UA tests: `Passed as part of full solution test; Configurator.Infrastructure.OpcUa.Tests, 35 passed`
- RouteMap UI tests: `Passed as part of full solution test; Configurator.Tests.RouteMap.Ui, 27 passed`
- Persistence tests: `Project not created`
- Sensitive-material scan over new Archiving source and test files: `Passed`

## Known limitations

- Stage 0 did not fix existing compiler/Avalonia warnings.
- Stage 1 did not implement archive storage, buffering, runtime collection, query execution, export, retention, authorization, licensing or UI features.
- Stage 1 did not start Stage 2.

## Next action

Stop here until Stage 2 is explicitly requested.
