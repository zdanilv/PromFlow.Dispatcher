# Testing And Diagnostics

Use this before PRs, commits or handoff to another agent.

## Basic Checks

```powershell
git status --short --branch
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
git diff --check
git status --short --branch
```

If build is blocked by a running `.NET Host` or stale `testhost`, stop the app/debug
session and rerun the targeted command.

## RouteMap Tests

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus Tests

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

## Persistence And Archive Tests

```powershell
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~Archive"
```

## Stage 14 Hardening Checks

Stage 14 production acceptance is recorded in
`AgentDocs/implementation/STAGE14-ACCEPTANCE.md`. Run the focused checks before the full
suite:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Hardening|FullyQualifiedName~Documentation|FullyQualifiedName~LicenseSessionExpiry|FullyQualifiedName~Licensing|FullyQualifiedName~Authorization|FullyQualifiedName~Workspace|FullyQualifiedName~Runtime"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~ArchiveProductionAcceptance|FullyQualifiedName~SessionRevocation|FullyQualifiedName~Archive|FullyQualifiedName~Security"
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~ModbusCommandAudit|FullyQualifiedName~ModbusArchive|FullyQualifiedName~Runtime"
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter "FullyQualifiedName~Archive|FullyQualifiedName~License|FullyQualifiedName~WorkspaceAuthorization"
rg -n "BinaryFormatter|\.Wait\(|\.Result|Thread\.Sleep" .\Configurator.Application .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Modbus .\Configurator.Desktop\Main .\Configurator.Desktop\Runtime .\Configurator.Desktop\Workspace
rg -n "BEGIN .*PRIVATE|PRIVATE KEY-----|AllowTestKeys\s*=\s*true|debug bypass|hardcoded password" .\Configurator.Application .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Modbus .\Configurator.Desktop .\Configurator.Boot
Get-ChildItem -Path . -Recurse -File -Include *.pem,*.promlicense,*.promrequest | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }
```

The source scans should return no matches. The file artifact scan should print nothing.

## SignalId <-> Modbus Diagnostics

| Symptom | Check |
|---|---|
| `Not configured` | Missing `Modbus.DataMap` point where `Name == SignalId` |
| Type error | `SignalValueType` versus Modbus `Type` |
| Access error | Binding direction versus point access |
| Address conflict | Duplicate coil/bit or overlapping registers |
| UI stays in mock | `RouteMapRuntime.SignalSource` and source setting |
| All values stale/offline | runtime state, poll interval, `StaleAfterMs` |
| Bit write rejected | Missing first raw holding-register snapshot |
| No readback | Access, PLC echo and `WriteConfirmationTimeoutMs` |
| Wrong physical address | Start address and PLC 0/1-based notation |

## Production Checklist

1. Verify login/bootstrap/logout.
2. Install or refresh the deployment license if commercial features are required.
3. Verify RouteMap in `Mock`.
4. Freeze the full SignalId list.
5. Obtain approved PLC coil/register/bit layout.
6. Confirm address notation.
7. Configure endpoint and start addresses in `Modbus Demo`.
8. Fill `Modbus.DataMap` in `SignalId <-> Modbus`.
9. Clear all unconfigured/error rows.
10. Enable read-only signals first.
11. Verify quality, stale and reconnect.
12. Verify modes, emergency, loader and target.
13. Enable equipment commands one by one.
14. Verify command audit for success, timeout, connection loss and pulse shutdown.
15. Verify Archive health, bounded query and bounded export.
16. Confirm interlocks and safety remain in PLC.
