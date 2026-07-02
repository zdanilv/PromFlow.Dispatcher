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
system.fault
route.node.bsu_1.active
route.node.bsu_1.target
route.node.bsu_1.loader
route.bsu2_to_bucket.fragment_1.active
equip.bucket.start
equip.bucket.start.off
equip.bucket.stop
equip.bucket.stop.off
equip.bucket.text
equip.bucket.parameter
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

Supported value types map to Modbus types as follows:

| SignalValueType | Modbus Type |
|---|---|
| `Bool` | `Bool` |
| `UInt16` | `UInt16` |
| `Word` | `Word` (`UInt16` remains compatible for old configs) |
| `Int16`, `Int32` | `Int` |
| `Dword` | `Dword` |
| `Float32` | `Real` |
| `String` | `String` |
| `Date` | `Date` |

## Roles

| Role | Type | Common use |
|---|---|---|
| `Visible` | `Bool` | visibility |
| `Fault` | `Bool` | alarm state |
| `ActiveRoute` | `Bool` | node/line route highlight |
| `ActiveRouteFragment` | `Bool` | split-line highlight |
| `Text` | string or number | card status |
| `Value` | supported value | extra runtime value |
| `EquipmentParameter` | supported value | configurable equipment-card parameter |
| `StartCommand`, `StopCommand` | `Bool` | equipment commands |
| `StartOffFeedback`, `StopOffFeedback` | `Bool` | card start/stop disable bits |
| `TargetCommand`, `LoaderCommand` | `Bool` | node menu commands |
| `AutomaticModeCommand`, `ManualModeCommand`, `EmergencyCommand` | `Bool` | TopBar commands |

`StartOffFeedback=true` or `StopOffFeedback=true` disables the matching card button and
visually resets `IsChecked=false` without writing a command. Other `*OffFeedback` roles
and `State` are legacy concepts and must not be used for new behavior.

`EquipmentParameter` is used only by the equipment-parameter list on a card. Admins set
`Title`, `SignalId`, `Direction`, and `ValueType` on the `Карточки` tab; these SignalIds
automatically appear in `SignalId ↔ Modbus`, while the physical address still belongs
only to `Modbus.DataMap`.

## System SignalIds

| SignalId | Type | Meaning |
|---|---|---|
| `connection.status` | `String` | Modbus runtime status text shown in TopBar |
| `connection.connected` | `Bool` | `true` when Modbus runtime is running and the snapshot is not stale |
| `system.fault` | `Bool` | PLC-mapped global RouteMap fault; `true` puts objects into fault visuals |

`connection.status` and `connection.connected` are produced by the provider. They appear
as internal system rows in `SignalId ↔ Modbus` and are not added to `Modbus.DataMap`.
`system.fault` appears in the same system group, but it is an ordinary PLC `Read/Bool`
input: create its `Modbus.DataMap` point explicitly.

## Adding A Signal

1. Add a RouteMap binding with role, direction, value type, and stable SignalId.
2. Apply or save the RouteMap definition.
3. Open `SignalId ↔ Modbus`.
4. Create a `Modbus.DataMap` point with the same `Name`.
5. Configure area, offset or physical address, bit, access, type, and write mode.
6. Verify diagnostics, first snapshot, command behavior, and readback.

For equipment-card parameters, add the signal through the card's `Настройки
оборудования` section. `Read` parameters are display-only in the dialog; `Write` and
`ReadWrite` parameters are sent through `SignalWriteRequest` when `Сохранить` is
pressed. Bool parameters use a switch, other values must match `SignalValueType`, and
the Modbus source requires the row to be configured in `SignalId ↔ Modbus` before
dispatch. `Word` has a dedicated Modbus `Word` type and remains compatible with old
`UInt16`, `Dword` is unsigned 32-bit, and `Date` is passed as `DateTime`. `String`
parameters require enough Modbus `Length`.

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
