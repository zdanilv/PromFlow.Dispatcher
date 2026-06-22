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

## Adding A Signal

1. Add a RouteMap binding with role, direction, value type, and stable SignalId.
2. Apply or save the RouteMap definition.
3. Open `SignalId ↔ Modbus`.
4. Create a `Modbus.DataMap` point with the same `Name`.
5. Configure area, offset or physical address, bit, access, type, and write mode.
6. Verify diagnostics, first snapshot, command behavior, and readback.

Do not change SignalId when moving a signal to another coil, register, or bit. Only
`Modbus.DataMap` changes.

