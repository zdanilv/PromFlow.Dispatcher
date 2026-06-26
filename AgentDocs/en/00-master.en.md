# Master: PromFlow.Dispatcher Agent Context

This directory contains the working context for agents changing `PromFlow.Dispatcher`.
Use `AgentDocs/en` for current English guidance, `AgentDocs/ru` for current Russian
guidance and `AgentDocs/source/ru` for migrated historical source guides.

## What To Read

| Task | Primary documents | Extra context |
|---|---|---|
| Change RouteMap UI | `02-route-map-guide.en.md`, `05-coding-rules.en.md` | `source/ru/route_map_programmer_guide.ru.md` |
| Connect PLC or Modbus TCP | `03-modbus-tcp-guide.en.md`, `04-signal-id-guide.en.md` | `source/ru/route_map_modbus_tcp_full_guide.ru.md` |
| Add a new signal | `04-signal-id-guide.en.md`, `03-modbus-tcp-guide.en.md` | `source/ru/signal_id_modbus_tcp_mapping_guide.ru.md` |
| Fix runtime, DI, or lifecycle | `01-architecture-overview.en.md`, `10-operations-and-recovery.en.md` | `source/ru/modbus_tcp_integration_guide.ru.md` |
| Work on archive | `07-archive-guide.en.md`, `06-testing-and-diagnostics.en.md` | implementation progress |
| Work on authorization | `08-authorization-guide.en.md`, `05-coding-rules.en.md` | implementation progress |
| Work on offline license | `09-offline-license-guide.en.md`, `08-authorization-guide.en.md` | implementation progress |
| Use the application after Stage 14 | `11-operator-user-guide.en.md`, `10-operations-and-recovery.en.md` | implementation progress |
| Issue or change licenses | `12-license-issuer-guide.en.md`, `09-offline-license-guide.en.md` | implementation progress |
| Inspect built-in SQLite databases or archive snapshots | `13-database-guide.en.md`, `07-archive-guide.en.md` | implementation progress |
| Verify before PR or commit | `06-testing-and-diagnostics.en.md`, `10-operations-and-recovery.en.md` | related source guides |

## Current Documents

- `01-architecture-overview.en.md` - layers, dynamic workspace, authorization, license, archive and lifecycle ownership.
- `02-route-map-guide.en.md` - RouteMap definition, schema v10, editor, migrations, validation and runtime state.
- `03-modbus-tcp-guide.en.md` - shared TCP runtime, `ModbusDemo`, `Modbus.DataMap`, snapshots and writes.
- `04-signal-id-guide.en.md` - SignalId naming, roles, directions, types and mapping.
- `05-coding-rules.en.md` - coding rules for the current architecture.
- `06-testing-and-diagnostics.en.md` - verification commands, diagnostics and Stage 14 acceptance checks.
- `07-archive-guide.en.md` - archive runtime, query, health, export, backup and retention operations.
- `08-authorization-guide.en.md` - login, session revocation, permissions and recovery authorization.
- `09-offline-license-guide.en.md` - offline license verification, installation and feature policy.
- `10-operations-and-recovery.en.md` - lifecycle operations, recovery checklist and manual endurance runbook.
- `11-operator-user-guide.en.md` - end-to-end operator guide for bootstrap, users, license, RouteMap, archive and troubleshooting.
- `12-license-issuer-guide.en.md` - exact LicenseIssuer commands, profile variants, feature rules and key handling.
- `13-database-guide.en.md` - SQLite database locations, safe inspection, archive snapshot queries and BLOB decoding.

## Core Invariants

- RouteMap UI uses domain `SignalId` values, not Modbus addresses.
- PLC physical addressing lives in `Modbus.DataMap`; never mix it with `ModbusDemo.DataMap`.
- `ModbusDemo` owns the TCP endpoint settings; centralized lifecycle owns runtime startup and shutdown.
- `RouteMapConfigurationManager` owns the active definition; do not register `RouteMapDefinition` as an immutable singleton.
- Modbus callbacks must not update Avalonia UI directly; use provider, mapper and ViewModel flow.
- UI visibility is not authorization. Enforce permissions and license features at service boundaries.
- User role and product license are independent inputs; Administrator does not bypass commercial features.
- Archive code must not depend on Avalonia, ReactiveUI or ViewModels.
- Store timestamps in UTC and keep queues bounded with deterministic shutdown.
- Emergency command delivery must not be blocked by archive unavailability.
- Safety interlocks, emergency behavior and final command acceptance remain in the PLC.
