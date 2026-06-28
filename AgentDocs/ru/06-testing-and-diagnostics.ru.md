# Проверка и диагностика

Используйте этот документ перед PR, commit или передачей работы другому агенту.

## Базовые проверки

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

Для документационных и solution metadata изменений минимум:

```powershell
rg --files -g '*.md'
dotnet build .\DesktopTemplate.slnx --no-restore
git status --short
```

Если build заблокирован running `.NET Host`, остановите запущенное приложение или debug
session и повторите build.

## RouteMap тесты

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus тесты

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

## Диагностика SignalId ↔ Modbus

| Симптом | Проверить |
|---|---|
| `Не настроен` | Нет точки `Modbus.DataMap` с `Name == SignalId` |
| Ошибка типа | `SignalValueType` и Modbus `Type` |
| Ошибка доступа | Binding direction и point access |
| Конфликт адреса | Duplicate coil/bit или пересекающиеся registers |
| UI остается в mock | `RouteMapRuntime.SignalSource` и флаг источника данных |
| User сбрасывает admin-настройки | Общий `%LOCALAPPDATA%\Configurator\appsettings.json` и overlay после exe-local defaults |
| Все stale/offline | Состояние `ModbusDemo`, poll interval и `StaleAfterMs` |
| В user-режиме видны вкладки | `Application.WorkMode` и binding `WorkspaceView.IsUserMode` |
| Offline lock не сработал | Системный `connection.connected`, mapper и `AreCommandsEnabled` |
| Bit-write отклонен | Нет первого raw snapshot holding register |
| Нет readback | Access, PLC echo и `WriteConfirmationTimeoutMs` |
| Неверный physical address | StartAddress и 0/1-based notation PLC |

## Production checklist

1. Проверить RouteMap в `Mock`.
2. Зафиксировать список всех SignalId.
3. Получить утвержденную PLC карту coils/registers/bits.
4. Уточнить notation адресов.
5. Настроить endpoint и start addresses в `Modbus Demo`.
6. Проверить, что `RouteMapRuntime`, `Modbus` и `ModbusDemo` сохранены в общем `%LOCALAPPDATA%\Configurator\appsettings.json`.
7. Заполнить `Modbus.DataMap` во вкладке `SignalId ↔ Modbus`.
8. Устранить все `Не настроен` и `Ошибка`.
9. Сначала включить read-only сигналы.
10. Проверить quality, stale, reconnect.
11. Проверить `connection.connected=false`: команды заблокированы, узлы/линии offline.
12. Проверить active nodes, active lines и fragments.
13. Проверить modes, emergency, loader/target.
14. По одной разрешить команды оборудования.
15. Проверить latched/pulse, timeout и потерю связи во время записи.
16. Убедиться, что interlock и safety реализованы в PLC.

