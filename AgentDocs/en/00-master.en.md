# Master: RouteMap/Modbus Agent Context

This directory contains the working context for agents changing `PromFlow.Dispatcher`.
Use `AgentDocs/en` for current English guidance and `AgentDocs/source/ru` for migrated
source guides with deeper Russian detail.

## What To Read

| Task | Primary documents | Extra context |
|---|---|---|
| Change RouteMap UI | `02-route-map-guide.en.md`, `05-coding-rules.en.md` | `source/ru/route_map_programmer_guide.ru.md` |
| Connect PLC or Modbus TCP | `03-modbus-tcp-guide.en.md`, `04-signal-id-guide.en.md` | `source/ru/route_map_modbus_tcp_full_guide.ru.md` |
| Add alarm dialogs or acknowledgements | `03-modbus-tcp-guide.en.md`, `06-testing-and-diagnostics.en.md` | `source/ru/modbus_tcp_integration_guide.ru.md` |
| Add a new signal | `04-signal-id-guide.en.md`, `03-modbus-tcp-guide.en.md` | `source/ru/signal_id_modbus_tcp_mapping_guide.ru.md` |
| Fix runtime, DI, or lifecycle | `01-architecture-overview.en.md`, `05-coding-rules.en.md` | `source/ru/modbus_tcp_integration_guide.ru.md` |
| Add UI icons in Avalonia | `07-svg-icons-guide.en.md` | `Material.Icons.Avalonia`, `Configurator.Desktop/Assets/icons` |
| Verify before PR or commit | `06-testing-and-diagnostics.en.md` | related source guides |

## Current Documents

- `01-architecture-overview.en.md` — Workspace, DI, RouteMap, SignalId, and Modbus runtime.
- `02-route-map-guide.en.md` — RouteMap definition, schema v11, editor, migrations, validation, runtime state.
- `03-modbus-tcp-guide.en.md` — shared TCP runtime, `ModbusDemo`, `Modbus.DataMap`, `Modbus.AlarmMap`, `Менеджер тревог` table, snapshots, writes.
- `04-signal-id-guide.en.md` — SignalId naming, roles, directions, types, and mapping.
- `05-coding-rules.en.md` — coding rules for the current architecture.
- `06-testing-and-diagnostics.en.md` — verification commands, diagnostics, production checklist.
- `07-svg-icons-guide.en.md` — choosing between Material Icons and embedded SVG files, and their Avalonia setup, colouring, and caching.

## Core Invariants

- RouteMap UI uses domain `SignalId` values, not Modbus addresses.
- PLC physical addressing lives in `Modbus.DataMap`; never mix it with `ModbusDemo.DataMap`.
- Operator alarm dialogs live in `Modbus.AlarmMap`, not `Modbus.DataMap`, but stay in the same `Modbus` config section.
- `system.fault` is a RouteMap global-fault SignalId and is configured in
  `Modbus.DataMap`, not in `Modbus.AlarmMap`.
- Card `Start`/`Stop` are mutually exclusive; `StartOffFeedback`/`StopOffFeedback` are
  active read-only roles for disabling those buttons.
- Card equipment parameters live in the RouteMap definition as `EquipmentParameter`
  SignalIds; their physical addresses are configured only in `Modbus.DataMap`.
- `ModbusDemo` owns the TCP endpoint and lifecycle for the shared runtime.
- `RouteMapConfigurationManager` owns the active definition; do not register `RouteMapDefinition` as an immutable singleton.
- Modbus callbacks must not update Avalonia UI directly; use provider, mapper, and ViewModel flow.
- `Start`, `Stop`, `Emergency`, loader/target, and mode commands are toggle/readback commands.
- Legacy `State` and non-card `*OffFeedback` roles must not be restored as current behavior.

