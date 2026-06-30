# Coding Rules

Follow these rules when changing RouteMap, Modbus TCP, SignalId mapping, or related UI.

## Architecture

- Do not put Modbus addresses in XAML, ViewModels, `RouteMapControl`, or `route-map.json`.
- Keep physical PLC mapping in `Modbus.DataMap`.
- Do not mix `Modbus.DataMap` with `ModbusDemo.DataMap`.
- Do not store operator alarm dialogs in `Modbus.DataMap`; use `Modbus.AlarmMap`.
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
- Card equipment parameters are stored on the card as `EquipmentParameter` bindings
  with configurable `Title`, `SignalId`, `Direction`, and `ValueType`.
- Do not restore old rails, sensors, item lists, vehicles, or bottom panels without a
  separate product requirement.

## SignalId

- Names must be stable and domain-oriented.
- Do not encode transport details in SignalId.
- Do not rename SignalId when only the PLC address changes.
- Required command bindings should stay `Bool` and normally `ReadWrite`.
- `ActiveRouteFragment` is always `Read` + `Bool`.
- `system.fault` is a system but PLC-mapped `Read/Bool` SignalId in `Modbus.DataMap`;
  do not move it to `Modbus.AlarmMap`.
- `StartOffFeedback` and `StopOffFeedback` are card-only `Read/Bool` roles. At `true`
  they disable the button and clear checked state without writing a command.
- `EquipmentParameter` is the only role for card equipment parameters. These SignalIds
  must flow into `SignalId ↔ Modbus` through `RouteMapSignalInventory`; do not maintain
  a separate manual list for them.

## Modbus

- RouteMap facade uses `Modbus.DataMap`; demo facade uses `ModbusDemo.DataMap`.
- `Modbus.AlarmMap` is read by the alarm monitor in admin/user modes; acknowledgements
  are written as bit pulses without service DataMap points.
- The right notification panel consumes the same `Modbus.AlarmMap` events through
  `RouteMapSessionJournal`; do not create service SignalIds for operator alarms.
- The `История` session tab stays in memory. Future database export should be wired
  through `ISessionJournalExporter`, not from XAML or Modbus callbacks.
- In `Менеджер тревог`, keep `Alarm` and `Acknowledgement` as different bits.
  `Repeat ms` is valid in `1000..86400000`, `Pulse ms` in `1..60000`; register bits are
  only `0..15`, and coil bits are not defined.
- Register-bit writes must preserve neighboring bits through shadow/read-modify-write.
- Do not automatically retry non-idempotent commands.
- Use `Pulse` only for confirmed PLC contracts.
- Lost connection should become quality/stale state, not a UI crash.

## UI And Threading

- Return observable Avalonia state updates to the UI thread.
- Dispose ViewModel subscriptions.
- Keep `NotificationsPanelView` at minimum width `400`, but do not set a fixed
  `MaxWidth`; the `История` tab must expand the RouteMap right column to table width.
- Runtime readback must not send commands back.
- The card-parameters dialog should write only `Write`/`ReadWrite` values through
  `IEquipmentCommandDispatcher`; `Read` rows are display-only, and `Сохранить` does not
  close the dialog. Show Bool as a switch; validate other values by `SignalValueType`,
  and check Modbus mapping before `DispatchAsync`.
- When `connection.connected=false`, cards show `Не в сети` with the muted indicator
  independently from their status/text binding.
- `Start`/`Stop` mutual exclusion writes `false` to the opposite command before `true`
  to the selected command; a readback conflict of two `true` values displays only `Stop`
  as checked.
- Optimistic UI state is temporary until readback arrives.

## Visual Studio Visibility

- Add new projects to `DesktopTemplate.slnx` with stable IDs.
- Include documentation files explicitly so Solution Explorer shows them.
- Add folder entries when useful for clear Visual Studio structure.
- For new Avalonia `.axaml`, follow the local designer metadata pattern.

