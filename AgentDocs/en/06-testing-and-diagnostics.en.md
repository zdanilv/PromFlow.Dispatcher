# Testing And Diagnostics

Use this before PRs, commits, or handoff to another agent.

## Basic Checks

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

For documentation and solution metadata changes, the minimum is:

```powershell
rg --files -g '*.md'
dotnet build .\DesktopTemplate.slnx --no-restore
git status --short
```

If build is blocked by a running `.NET Host`, stop the app/debug session and rebuild.

## RouteMap Tests

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus Tests

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

## SignalId ↔ Modbus Diagnostics

| Symptom | Check |
|---|---|
| `Not configured` | Missing `Modbus.DataMap` point where `Name == SignalId` |
| Type error | `SignalValueType` versus Modbus `Type` |
| Access error | Binding direction versus point access |
| Address conflict | Duplicate coil/bit or overlapping registers |
| UI stays in mock | `RouteMapRuntime.SignalSource` and source setting |
| All values stale/offline | `ModbusDemo` state, poll interval, `StaleAfterMs` |
| Bit write rejected | Missing first raw holding-register snapshot |
| No readback | Access, PLC echo, `WriteConfirmationTimeoutMs` |
| Wrong physical address | Start address and PLC 0/1-based notation |

## Production Checklist

1. Verify RouteMap in `Mock`.
2. Freeze the full SignalId list.
3. Obtain approved PLC coil/register/bit layout.
4. Confirm address notation.
5. Configure endpoint and start addresses in `Modbus Demo`.
6. Fill `Modbus.DataMap` in `SignalId ↔ Modbus`.
7. Clear all unconfigured/error rows.
8. Enable read-only signals first.
9. Verify quality, stale, and reconnect.
10. Verify active nodes, lines, and fragments.
11. Verify modes, emergency, loader, and target.
12. Enable equipment commands one by one.
13. Verify latched/pulse behavior, timeout, and connection loss during write.
14. Confirm interlocks and safety remain in PLC.

