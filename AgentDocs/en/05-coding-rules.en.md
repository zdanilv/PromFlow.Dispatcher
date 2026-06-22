# Coding Rules

Follow these rules when changing RouteMap, Modbus TCP, SignalId mapping, or related UI.

## Architecture

- Do not put Modbus addresses in XAML, ViewModels, `RouteMapControl`, or `route-map.json`.
- Keep physical PLC mapping in `Modbus.DataMap`.
- Do not mix `Modbus.DataMap` with `ModbusDemo.DataMap`.
- Do not move TCP endpoint/lifecycle ownership from `ModbusDemo` into RouteMap.
- Do not register `RouteMapDefinition` as an immutable singleton.
- Do not update Avalonia UI directly from Modbus callbacks.
- Keep interlocks, safety, and final actuator permissions in PLC logic.

## RouteMap

- Topology and visual settings belong in definition/seed/configuration.
- `RouteMapControl` should draw and surface UI commands, not own business logic.
- Runtime states should flow through `RouteMapRuntimeMapper`.
- New bindings must be validated by `RouteMapConfigurationValidator`.
- Schema changes require migrator updates and tests.
- Do not restore old rails, sensors, item lists, vehicles, or bottom panels without a
  separate product requirement.

## SignalId

- Names must be stable and domain-oriented.
- Do not encode transport details in SignalId.
- Do not rename SignalId when only the PLC address changes.
- Required command bindings should stay `Bool` and normally `ReadWrite`.
- `ActiveRouteFragment` is always `Read` + `Bool`.

## Modbus

- RouteMap facade uses `Modbus.DataMap`; demo facade uses `ModbusDemo.DataMap`.
- Register-bit writes must preserve neighboring bits through shadow/read-modify-write.
- Do not automatically retry non-idempotent commands.
- Use `Pulse` only for confirmed PLC contracts.
- Lost connection should become quality/stale state, not a UI crash.

## UI And Threading

- Return observable Avalonia state updates to the UI thread.
- Dispose ViewModel subscriptions.
- Runtime readback must not send commands back.
- Optimistic UI state is temporary until readback arrives.

## Visual Studio Visibility

- Add new projects to `DesktopTemplate.slnx` with stable IDs.
- Include documentation files explicitly so Solution Explorer shows them.
- Add folder entries when useful for clear Visual Studio structure.
- For new Avalonia `.axaml`, follow the local designer metadata pattern.

