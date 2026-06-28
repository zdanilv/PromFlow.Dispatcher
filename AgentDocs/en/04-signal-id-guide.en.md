# SignalId Guide

`SignalId` is a stable domain signal name. It describes meaning, not a PLC physical
address. RouteMap, ViewModels, and XAML should know only `SignalId`, role, direction, and
value type.

## Naming

Recommended format:

```text
<area>.<object>.<property-or-command>
```

Examples:

```text
system.mode.automatic
system.mode.manual
system.emergency
route.node.bsu_1.active
route.node.bsu_1.target
route.node.bsu_1.loader
route.bsu2_to_bucket.fragment_1.active
equip.bucket.start
equip.bucket.stop
equip.bucket.text
connection.status
connection.connected
```

Use lowercase Latin letters, digits, dots, and `_` inside object identifiers. Do not encode
transport details such as `coil_15` or `register_40003_bit_2`.

## Binding

Each binding has:

| Field | Meaning |
|---|---|
| `Role` | How RouteMap uses the signal |
| `SignalId` | Domain name |
| `Direction` | `Read`, `Write`, or `ReadWrite` |
| `ValueType` | Expected value type |

Direction must match Modbus access:

| Direction | Required access |
|---|---|
| `Read` | `Read` or `ReadWrite` |
| `Write` | `Write` or `ReadWrite` |
| `ReadWrite` | `ReadWrite` |

## Roles

| Role | Type | Common use |
|---|---|---|
| `Visible` | `Bool` | visibility |
| `Fault` | `Bool` | alarm state |
| `ActiveRoute` | `Bool` | node/line route highlight |
| `ActiveRouteFragment` | `Bool` | split-line highlight |
| `Text` | string or number | card status |
| `Value` | supported value | extra runtime value |
| `StartCommand`, `StopCommand` | `Bool` | equipment commands |
| `TargetCommand`, `LoaderCommand` | `Bool` | node menu commands |
| `AutomaticModeCommand`, `ManualModeCommand`, `EmergencyCommand` | `Bool` | TopBar commands |

`State` and `*OffFeedback` are legacy concepts and must not be used for new behavior.

## System SignalIds

| SignalId | Type | Meaning |
|---|---|---|
| `connection.status` | `String` | Modbus runtime status text shown in TopBar |
| `connection.connected` | `Bool` | `true` when Modbus runtime is running and the snapshot is not stale |

System SignalIds are produced by the provider. They appear as system rows in
`SignalId ↔ Modbus` and are not added to `Modbus.DataMap`.

## Adding A Signal

1. Add a RouteMap binding with role, direction, value type, and stable SignalId.
2. Apply or save the RouteMap definition.
3. Open `SignalId ↔ Modbus`.
4. Create a `Modbus.DataMap` point with the same `Name`.
5. Configure area, offset or physical address, bit, access, type, and write mode.
6. Verify diagnostics, first snapshot, command behavior, and readback.

Do not change SignalId when moving a signal to another coil, register, or bit. Only
`Modbus.DataMap` changes.

Operator alarms and repeated-confirmation warnings that are not RouteMap binding roles do
not get SignalIds and are not added to `Modbus.DataMap`. Configure them through
`Менеджер тревог`, which saves `Modbus.AlarmMap`.

In the `Менеджер тревог` table, `Alarm area/Offset/Bit` define the input bit that opens
the dialog, while `OK area/Offset/Bit` define the separate acknowledgement bit written by
`Хорошо`. `Alarm client/server` and `OK client/server` show physical addresses based on
`ModbusDemo.Client/Server` and can recalculate area/offset when edited. `Repeat ms`
controls dialog repetition while `Alarm=true`; `Pulse ms` controls the `true/false`
acknowledgement pulse length. `HoldingRegister` requires `Bit=0..15`; `Coil` does not
use a bit index.
