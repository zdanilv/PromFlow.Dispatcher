# Modbus TCP Guide

RouteMap uses Modbus TCP through domain `SignalId` values. Physical addressing is stored
in `Modbus.DataMap`; endpoint and lifecycle belong to `ModbusDemo`.

## Data Map Separation

| Map | Purpose |
|---|---|
| `Modbus.DataMap` | Production RouteMap `SignalId` mapping to coils/registers/bits |
| `ModbusDemo.DataMap` | Controls on the `Modbus Demo` screen only |

Never add RouteMap SignalIds to `ModbusDemo.DataMap`.

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

`connection.status` is a system SignalId produced by the runtime provider. Do not add it
to `DataMap`.

## Writes

`Latched` writes the supplied value and waits for readback for readable points.
`Pulse` accepts only `true`, writes `true`, waits `PulseDurationMs`, then writes `false`.
Use pulse only when the PLC contract requires an edge or short pulse.

Register-bit writes are serialized read-modify-write operations using the latest raw word
shadow. The first write is rejected until the raw register snapshot exists. Do not map the
same bit twice or overlap whole-register values with bit points.

