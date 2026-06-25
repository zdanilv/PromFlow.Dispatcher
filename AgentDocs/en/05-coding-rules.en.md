# Coding Rules

Follow these rules when changing RouteMap, Modbus TCP, SignalId mapping, archive,
authorization, licensing or lifecycle code.

## Architecture

- Do not put Modbus addresses in XAML, ViewModels, `RouteMapControl` or `route-map.json`.
- Keep physical PLC mapping in `Modbus.DataMap`.
- Do not mix `Modbus.DataMap` with `ModbusDemo.DataMap`.
- Keep TCP endpoint settings under `ModbusDemo`; keep startup/shutdown under centralized lifecycle.
- Do not register `RouteMapDefinition` as an immutable singleton.
- Do not update Avalonia UI directly from Modbus callbacks.
- Keep interlocks, safety and final actuator permissions in PLC logic.

## Authorization And License

- UI visibility is not authorization.
- Protect direct service boundaries with `IAccessDecisionService` or a dedicated authorized wrapper.
- Keep role permission and license feature checks independent.
- Do not let Administrator bypass commercial license features.
- Clear the active session when the current user is disabled through application services.
- Never add hardcoded passwords, private keys, generated licenses or debug bypasses.

## Archive

- Archive code must not depend on Avalonia, ReactiveUI or ViewModels.
- Do not execute SQL in Modbus callbacks.
- Keep archive queues bounded and shutdown deterministic.
- Do not serialize each Modbus register as a separate hot-path SQL row.
- Use paged/cancelable queries for operator views.
- Decode raw snapshot BLOB values only for explicit details requests.
- Emergency command delivery must not be blocked by archive unavailability.

## RouteMap

- Topology and visual settings belong in definition/seed/configuration.
- `RouteMapControl` should draw and surface UI commands, not own business logic.
- Runtime states should flow through `RouteMapRuntimeMapper`.
- New bindings must be validated by `RouteMapConfigurationValidator`.
- Schema changes require migrator updates and tests.
- Do not restore old rails, sensors, item lists, vehicles or bottom panels without a separate product requirement.

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
- Lost connection should become quality/stale state and terminal audit evidence, not a UI crash.

## UI And Threading

- Return observable Avalonia state updates to the UI thread.
- Dispose ViewModel subscriptions.
- Runtime readback must not send commands back.
- Do not block with `.Wait()`, task `.Result`, `Thread.Sleep`, SQL or file I/O on the UI path.
- Avoid `async void` except framework event handlers.
- Propagate cancellation tokens through service calls.

## Visual Studio Visibility

- Add new projects to `DesktopTemplate.slnx` with stable IDs.
- Include documentation files through `AgentDocs/AgentDocs.csproj`.
- Add folder entries when useful for clear Visual Studio structure.
- For new Avalonia `.axaml`, follow the local designer metadata pattern.
