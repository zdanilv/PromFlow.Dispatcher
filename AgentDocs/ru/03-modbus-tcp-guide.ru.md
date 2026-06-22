# Modbus TCP guide

RouteMap использует Modbus TCP через доменные `SignalId`. Физическая адресация находится
в `Modbus.DataMap`, а endpoint и lifecycle принадлежат `ModbusDemo`.

## Разделение карт

| Карта | Назначение |
|---|---|
| `Modbus.DataMap` | Production mapping RouteMap `SignalId` к coils/registers/bits |
| `ModbusDemo.DataMap` | Только controls экрана `Modbus Demo` |

Не добавляйте RouteMap SignalId в `ModbusDemo.DataMap`. Не используйте demo-точки как
production mapping.

## Endpoint и lifecycle

`ModbusDemo` задает:

- Client host, port, UnitId;
- Server bind address, port, UnitId;
- coil/register start addresses и counts;
- poll interval;
- autostart и startup mode.

Переключение RouteMap на `SignalSource=Modbus` не запускает TCP runtime. Runtime запускается
на вкладке `Modbus Demo` или через `ModbusDemo.AutostartOnWorkspaceOpen`.

## DataMap point

Пример coil:

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

Пример bool в holding register:

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

`Name` должен точно соответствовать `SignalBinding.SignalId`. Сравнение выполняется без
учета регистра, но используйте единое написание.

## Address и physical address

`DataMap.Address` — zero-based offset относительно start address активного endpoint.

```text
physical coil address     = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

Перед вводом production-адресов уточните, использует ли PLC-документация 0-based,
1-based или notation вроде `40001`.

## Snapshots и quality

Клиент или сервер публикует `ModbusSnapshot`. RouteMap facade декодирует snapshot по
`Modbus.DataMap` и публикует значения. Если runtime остановлен, связь потеряна или значение
устарело, RouteMap получает bad/stale quality, а не аварийное завершение приложения.

`connection.status` — системный SignalId. Его создает runtime provider; добавлять его в
`DataMap` не нужно.

## Запись

`ModbusTcpCommandDispatcher` проверяет:

- наличие `SignalId` в `Modbus.DataMap`;
- writable access;
- совместимость `SignalValueType` и Modbus type;
- корректность значения и адреса.

`Latched` записывает переданное значение и для readable-точек ждет readback до
`WriteConfirmationTimeoutMs`.

`Pulse` принимает только `true`, пишет `true`, ждет `PulseDurationMs`, затем пытается
записать `false`. Используйте pulse только для PLC-контрактов, где нужен фронт или
короткий импульс.

## Register-bit write

Bool внутри holding register пишется как сериализованный read-modify-write. Перед первой
записью должен быть получен raw snapshot слова; иначе возвращается
`ModbusRegisterShadowUnavailable`.

Не назначайте один и тот же bit двум SignalId. Не пересекайте целое register-значение со
словом, которое используется как набор bit-точек.

