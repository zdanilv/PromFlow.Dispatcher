# Обзор архитектуры

`PromFlow.Dispatcher` — Avalonia desktop-приложение с разделением на Boot, Desktop,
Application и Infrastructure. RouteMap находится в desktop-слое, но transport/runtime
интеграция вынесена в application contracts и Modbus infrastructure.

## Workspace

Актуальный `WorkspaceView` содержит три вкладки:

```text
Route Map
SignalId ↔ Modbus
Modbus Demo
```

`WorkspaceViewModel` получает через DI:

- `RouteMapDashboardViewModel` — операторская мнемосхема.
- `RouteMapSignalMappingViewModel` — редактор связей `SignalId ↔ Modbus`.
- `ModbusDemoViewModel` — экран запуска, остановки и настройки общего TCP runtime.

`ModbusDemo.AutostartOnWorkspaceOpen` и `StartupMode` могут запускать общий runtime при
открытии Workspace.

## Поток чтения

```text
ModbusDemo endpoint/lifecycle
  -> shared ModbusRuntimeService
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> Avalonia UI
```

RouteMap получает не coils/registers, а `SignalValue` по `SignalId`. Mapper переводит
значения, quality и stale-состояния в `RouteMapRuntimeState`.

## Поток записи

```text
TopBar / node menu / equipment card
  -> SignalWriteRequest
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService.SetAsync
  -> shared Modbus client or local server
  -> poll/readback
```

UI может оптимистично обновить checked-состояние, но окончательная синхронизация приходит
через readback. Входной `ReadWrite` сигнал не должен порождать повторную запись.

## DI и владельцы состояния

- `Configurator.Boot/Program.cs` собирает desktop DI и переключаемый `IRouteMapSignalRuntime`.
- `Configurator.Infrastructure.Modbus/DependencyInjection.cs` регистрирует общий runtime,
  RouteMap facade и demo facade.
- `RouteMapConfigurationManager` — единственный владелец `CurrentDocument` и
  `CurrentDefinition`.
- `RouteMapConfigurationStorage`, `Migrator`, `Validator` и `Mapper` обслуживают
  загрузку, миграцию, проверку и преобразование JSON-профиля.
- `MockSignalProvider` и `ModbusTcpSignalValueProvider` читают актуальную definition через manager.

## Modbus runtime

`ModbusRuntimeService` координирует client/server роли. `ModbusDemo` управляет endpoint,
port, UnitId, start addresses, autostart и lifecycle. RouteMap использует тот же runtime,
но отдельную карту данных.

```text
ModbusDemo.DataMap -> demo UI facade
Modbus.DataMap     -> RouteMap facade and SignalId mapping tab
```

Не переносите endpoint/lifecycle в RouteMap. RouteMap отвечает за доменные bindings и
интерпретацию сигналов, а не за физическое подключение PLC.

## Safety boundary

UI не является контуром функциональной безопасности. Interlock, аварийная логика,
разрешения исполнительных механизмов и окончательное принятие команд должны оставаться
в PLC.

