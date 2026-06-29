# Modbus TCP Guide

RouteMap uses Modbus TCP through domain `SignalId` values. Physical addressing is stored
in `Modbus.DataMap`; endpoint and lifecycle belong to `ModbusDemo`.

Mutable `Modbus` and `ModbusDemo` sections are saved in the shared
`%LOCALAPPDATA%\Configurator\appsettings.json`. Admin mode exposes them through
`SignalId ↔ Modbus`, `Менеджер тревог`, and `Modbus Demo`; user mode hides those tabs
while the runtime, autostart, mappings, and alarm dialogs keep using the same saved values.

## Data Map Separation

| Map | Purpose |
|---|---|
| `Modbus.DataMap` | Production RouteMap `SignalId` mapping to coils/registers/bits |
| `Modbus.AlarmMap` | User alarm/confirmation dialogs and acknowledgement bits |
| `ModbusDemo.DataMap` | Controls on the `Modbus Demo` screen only |

Never add RouteMap SignalIds to `ModbusDemo.DataMap`.
Never add operator alarms to `Modbus.DataMap`: use `Modbus.AlarmMap` so RouteMap does not
see alarms as SignalIds.
`system.fault` is the named exception in the opposite direction: it is not an operator
dialog, but a RouteMap global-fault SignalId, so it belongs in `Modbus.DataMap`.

## Endpoint And Lifecycle

`ModbusDemo` owns client/server address, port, UnitId, start addresses, counts, poll
interval, autostart, and startup mode. Switching RouteMap to `SignalSource=Modbus` does
not start TCP by itself.

## Data Point

Coil example:

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

Bool inside holding register:

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

`Name` must match `SignalBinding.SignalId`. Keep one canonical casing even though lookup
is case-insensitive.

## AlarmMap Entry

An alarm entry has one alarm bit and a separate acknowledgement bit:

```json
{
  "Id": "alarm.main",
  "Enabled": true,
  "Kind": "Fault",
  "Message": "Drive fault",
  "Alarm": { "Area": "HoldingRegister", "Address": 5, "BitIndex": 0 },
  "Acknowledgement": { "Area": "HoldingRegister", "Address": 5, "BitIndex": 1 },
  "RepeatIntervalMs": 60000,
  "AcknowledgementPulseDurationMs": 300
}
```

`ModbusAlarmMonitor` runs while Workspace is open in both admin and user modes. It shows
the dialog when `Alarm` rises to `true`. `Хорошо` writes a `true/false` acknowledgement
pulse; if the alarm bit remains `true`, the dialog repeats after `RepeatIntervalMs`.

### `Менеджер тревог` Table

The tab edits only `Modbus.AlarmMap`. `ДОБАВИТЬ` creates an enabled row with
`Kind=Fault`, `Alarm=Coil[0]`, `Acknowledgement=Coil[1]`, `RepeatIntervalMs=60000`, and
`AcknowledgementPulseDurationMs=300`. `СОХРАНИТЬ` writes only `AlarmMap` and does not
change `DataMap`; `ПЕРЕЗАГРУЗИТЬ` reloads the saved map and discards the local draft.

| Column | Config field | Purpose |
|---|---|---|
| `Вкл.` | `Enabled` | Enables the row for the alarm monitor; disabled rows are saved but do not show dialogs or write acknowledgement |
| `Id` | `Id` | Unique, non-empty alarm identifier used for repeat state |
| `Тип` | `Kind` | `Fault` shows the red `Авария` dialog; `Confirmation` shows the warning-style `Повторное подтверждение` dialog; `Message` shows the neutral `Сообщение` dialog |
| `Сообщение` | `Message` | Text shown to the operator in the modal dialog |
| `Alarm area` | `Alarm.Area` | Input bit area: `Coil` or `HoldingRegister` |
| `Offset` after `Alarm area` | `Alarm.Address` | Zero-based alarm-bit offset inside the selected area |
| `Bit` after `Alarm area` | `Alarm.BitIndex` | Bit `0..15` for `HoldingRegister`; unused for `Coil` |
| `Alarm client` | derived from `Alarm.*` and `ModbusDemo.Client` | Alarm physical address for client start addresses; editing it recalculates `Alarm.Area` and `Alarm.Address` |
| `Alarm server` | derived from `Alarm.*` and `ModbusDemo.Server` | Alarm physical address for server start addresses; editing it recalculates `Alarm.Area` and `Alarm.Address` |
| `OK area` | `Acknowledgement.Area` | Separate acknowledgement bit area written by `Хорошо` |
| `Offset` after `OK area` | `Acknowledgement.Address` | Zero-based acknowledgement-bit offset |
| `Bit` after `OK area` | `Acknowledgement.BitIndex` | Bit `0..15` for acknowledgement in `HoldingRegister`; unused for `Coil` |
| `OK client` | derived from `Acknowledgement.*` and `ModbusDemo.Client` | Acknowledgement physical address for client start addresses |
| `OK server` | derived from `Acknowledgement.*` and `ModbusDemo.Server` | Acknowledgement physical address for server start addresses |
| `Repeat ms` | `RepeatIntervalMs` | Repeat interval while the alarm bit remains `true`; valid range `1000..86400000` |
| `Pulse ms` | `AcknowledgementPulseDurationMs` | `true/false` acknowledgement pulse duration; valid range `1..60000` |
| `Действие` | — | `Копия` duplicates the row with a new `Id`; `Удалить` removes it from the draft |

`Alarm` and `Acknowledgement` must point to different bits and fit the active endpoint
ranges from `ModbusDemo`. Closing the dialog with `X` does not acknowledge; the pulse is
sent only from `Хорошо`.

## Addressing

`DataMap.Address` is a zero-based offset from the active endpoint start address:

```text
physical coil address     = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

Confirm whether PLC documentation uses 0-based, 1-based, or `40001`-style notation before
entering production addresses.

## Snapshots And Quality

Client/server roles publish `ModbusSnapshot`. The RouteMap facade decodes snapshots
through `Modbus.DataMap`. Stopped runtime, lost connection, or stale values produce
quality/stale state instead of crashing the UI.

`connection.status` and `connection.connected` are system SignalIds produced by the
runtime provider. Do not add them to `DataMap`. `connection.connected=false` disables
RouteMap commands and forces nodes/segments into offline state.

`system.fault` is a system-row but PLC-mapped SignalId. Create a `Read/Bool`
`Modbus.DataMap` point for it in `SignalId ↔ Modbus`; when it is `true`, RouteMap objects
and cards use the global fault visual state.

## Writes

`Latched` writes the supplied value and waits for readback for readable points.
`Pulse` accepts only `true`, writes `true`, waits `PulseDurationMs`, then writes `false`.
Use pulse only when the PLC contract requires an edge or short pulse.

Register-bit writes are serialized read-modify-write operations using the latest raw word
shadow. The first write is rejected until the raw register snapshot exists. Do not map the
same bit twice or overlap whole-register values with bit points.
