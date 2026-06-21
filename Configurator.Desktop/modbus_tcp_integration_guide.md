# Подключение Modbus TCP к RouteMap

## Архитектура

RouteMap работает только с доменными `SignalId` и не хранит IP, UnitId или физические
адреса в `route-map.json`, XAML и ViewModel.

```text
ModbusDemo screen
  -> IModbusDemoTcpService
  -> shared ModbusRuntimeService / client / server

shared runtime snapshots
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> RouteMapRuntimeMapper
  -> UI

RouteMap UI commands
  -> ModbusTcpCommandDispatcher
  -> RouteMap IModbusTcpService facade
  -> shared ModbusRuntimeService / client / server
```

`ModbusDemo` является единственным экраном запуска, остановки и настройки Modbus TCP
подключения. `ModbusDemo.Client`, `ModbusDemo.Server`, `AutostartOnWorkspaceOpen` и
`StartupMode` задают endpoint и lifecycle.

Карты данных разделены:

- `ModbusDemo.DataMap` используется только контролами экрана `Modbus Demo`;
- `Modbus.DataMap` используется RouteMap и вкладкой `SignalId ↔ Modbus`;
- обе карты читают и пишут через общий TCP runtime.

В Workspace остаются только вкладки `Route Map`, `SignalId ↔ Modbus`, `Modbus Demo`.

## Выбор Источника RouteMap

```json
"RouteMapRuntime": {
  "SignalSource": "Mock",
  "StaleAfterMs": 1500
}
```

- `Mock` использует встроенную симуляцию и не требует PLC;
- `Modbus` использует общий demo runtime, но декодирует snapshot по `Modbus.DataMap`.

Источник переключается в `Route Map` -> `НАСТРОЙКИ` -> `Источник данных`.
`ПРИМЕНИТЬ` меняет текущую сессию, `СОХРАНИТЬ` также обновляет `RouteMapRuntime` в
`appsettings.json`. Переключение на Modbus не запускает соединение: запуск выполняется
на вкладке `Modbus Demo`.

Связи SignalId с адресами редактируются на вкладке `SignalId ↔ Modbus`.

## Каталог SignalId

`ModbusDataPointOptions.Name` должен точно совпадать с `SignalBinding.SignalId`.
`Address` хранится как нулевое смещение относительно `ModbusDemo.Client` или
`ModbusDemo.Server` start address.

Обычный Coil:

```json
{
  "Name": "system.emergency",
  "Area": "Coil",
  "Address": 0,
  "Length": 1,
  "Access": "ReadWrite",
  "Type": "Bool",
  "WriteMode": "Latched"
}
```

Bool внутри Holding Register:

```json
{
  "Name": "equip.bucket.start",
  "Area": "HoldingRegister",
  "Address": 3,
  "Length": 1,
  "BitIndex": 0,
  "Access": "ReadWrite",
  "Type": "Bool",
  "WriteMode": "Pulse",
  "PulseDurationMs": 300
}
```

Для register-bit точки обязательны `Type=Bool`, `Length=1` и `BitIndex=0..15`.
Запись выполняется как сериализованный read-modify-write.

## Семантика Команд

В schema v8 карточные `ПУСК`/`СТОП` и TopBar `АВАРИЯ` всегда работают как
toggle-команды RouteMap и пишут `true/false` в свои command bindings. Legacy-значение
`RouteCommandButtonKind.Momentary` миграция приводит к `Toggle`.

`ModbusWriteMode` управляет только физической записью:

- `ModbusWriteMode.Latched` физически записывает переданное значение;
- `ModbusWriteMode.Pulse` физически пишет `true`, ждет `PulseDurationMs`, затем пишет `false`.

`Pulse` можно выбрать вручную для нужной точки `Modbus.DataMap`; новые mapping больше
не получают pulse-режим автоматически по типу кнопки.

## Ввод В Эксплуатацию

1. Оставить `SignalSource=Mock` и проверить RouteMap UI.
2. Настроить endpoint и lifecycle на вкладке `Modbus Demo`.
3. Заполнить `Modbus.DataMap` на вкладке `SignalId ↔ Modbus`.
4. Заполнить `ModbusDemo.DataMap` только для контролов demo-экрана.
5. Запустить Client или Server на вкладке `Modbus Demo`.
6. Переключить RouteMap на `SignalSource=Modbus`.
7. Проверить readback, timeout, reconnect и interlock на стенде.

UI не является контуром функциональной безопасности. Interlock режимов, аварии и
исполнительных механизмов должен оставаться в PLC.

## Проверка

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```
