# Проверка и диагностика

Используйте этот документ перед PR, commit или передачей работы другому агенту.

## Базовые проверки

```powershell
git status --short --branch
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
git diff --check
git status --short --branch
```

Если build заблокирован running `.NET Host` или stale `testhost`, остановите приложение
или debug session и повторите targeted command.

## RouteMap tests

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus tests

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

## Persistence и Archive tests

```powershell
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~Archive"
```

## Stage 14 hardening checks

Stage 14 production acceptance фиксируется в
`AgentDocs/implementation/STAGE14-ACCEPTANCE.md`. Перед full suite выполните focused
checks:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~Hardening|FullyQualifiedName~Documentation|FullyQualifiedName~LicenseSessionExpiry|FullyQualifiedName~Licensing|FullyQualifiedName~Authorization|FullyQualifiedName~Workspace|FullyQualifiedName~Runtime"
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore --filter "FullyQualifiedName~ArchiveProductionAcceptance|FullyQualifiedName~SessionRevocation|FullyQualifiedName~Archive|FullyQualifiedName~Security"
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~ModbusCommandAudit|FullyQualifiedName~ModbusArchive|FullyQualifiedName~Runtime"
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter "FullyQualifiedName~Archive|FullyQualifiedName~License|FullyQualifiedName~WorkspaceAuthorization"
rg -n "BinaryFormatter|\.Wait\(|\.Result|Thread\.Sleep" .\Configurator.Application .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Modbus .\Configurator.Desktop\Main .\Configurator.Desktop\Runtime .\Configurator.Desktop\Workspace
rg -n "BEGIN .*PRIVATE|PRIVATE KEY-----|AllowTestKeys\s*=\s*true|debug bypass|hardcoded password" .\Configurator.Application .\Configurator.Infrastructure .\Configurator.Infrastructure.Persistence .\Configurator.Infrastructure.Modbus .\Configurator.Desktop .\Configurator.Boot
Get-ChildItem -Path . -Recurse -File -Include *.pem,*.promlicense,*.promrequest | Where-Object { $_.FullName -notmatch '\\(bin|obj|\.git)\\' }
```

Source scans должны вернуть no matches. File artifact scan должен ничего не вывести.

## Диагностика SignalId <-> Modbus

| Симптом | Проверить |
|---|---|
| `Not configured` | Нет точки `Modbus.DataMap` с `Name == SignalId` |
| Type error | `SignalValueType` против Modbus `Type` |
| Access error | Binding direction против point access |
| Address conflict | Duplicate coil/bit или overlapping registers |
| UI остается в mock | `RouteMapRuntime.SignalSource` и source setting |
| Все values stale/offline | runtime state, poll interval, `StaleAfterMs` |
| Bit write rejected | Нет первого raw snapshot holding register |
| No readback | Access, PLC echo и `WriteConfirmationTimeoutMs` |
| Wrong physical address | Start address и PLC 0/1-based notation |

## Production checklist

1. Проверить login/bootstrap/logout.
2. Установить или refresh deployment license, если нужны commercial features.
3. Проверить RouteMap в `Mock`.
4. Зафиксировать полный SignalId list.
5. Получить утвержденную PLC map coils/registers/bits.
6. Уточнить notation addresses.
7. Настроить endpoint и start addresses в `Modbus Demo`.
8. Заполнить `Modbus.DataMap` во вкладке `SignalId <-> Modbus`.
9. Очистить все unconfigured/error rows.
10. Сначала включить read-only signals.
11. Проверить quality, stale и reconnect.
12. Проверить modes, emergency, loader и target.
13. Включать equipment commands по одной.
14. Проверить command audit для success, timeout, connection loss и pulse shutdown.
15. Проверить Archive health, bounded query и bounded export.
16. Убедиться, что interlocks и safety остаются в PLC.
