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
| Все stale/offline | Состояние `ModbusDemo`, poll interval и `StaleAfterMs` |
| Bit-write отклонен | Нет первого raw snapshot holding register |
| Нет readback | Access, PLC echo и `WriteConfirmationTimeoutMs` |
| Неверный physical address | StartAddress и 0/1-based notation PLC |

## Production checklist

1. Проверить RouteMap в `Mock`.
2. Зафиксировать список всех SignalId.
3. Получить утвержденную PLC карту coils/registers/bits.
4. Уточнить notation адресов.
5. Настроить endpoint и start addresses в `Modbus Demo`.
6. Заполнить `Modbus.DataMap` во вкладке `SignalId ↔ Modbus`.
7. Устранить все `Не настроен` и `Ошибка`.
8. Сначала включить read-only сигналы.
9. Проверить quality, stale, reconnect.
10. Проверить active nodes, active lines и fragments.
11. Проверить modes, emergency, loader/target.
12. По одной разрешить команды оборудования.
13. Проверить latched/pulse, timeout и потерю связи во время записи.
14. Убедиться, что interlock и safety реализованы в PLC.

