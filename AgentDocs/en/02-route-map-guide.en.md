# RouteMap Guide

RouteMap is the first Workspace tab and the operator route diagram. It stores topology,
visual settings, and bindings in the RouteMap definition, but it does not store physical
Modbus addresses.

The active RouteMap definition is shared through
`%LOCALAPPDATA%\Configurator\RouteMap\route-map.json`. The runtime source
`RouteMapRuntime.SignalSource` is saved in the shared
`%LOCALAPPDATA%\Configurator\appsettings.json`, so switching from mock to Modbus in
admin mode is preserved for the next user-mode launch.

## Main Areas

| Area | Location |
|---|---|
| Screen | `Configurator.Desktop/Workspace/RouteMap/RouteMapDashboardView.axaml` |
| Map drawing | `Workspace/RouteMap/Controls/RouteMapControl.cs` |
| Segment geometry | `Workspace/RouteMap/Controls/RouteSegmentGeometry.cs` |
| Attached cards | `Workspace/RouteMap/Panels/RouteMapAttachedCardsLayer.cs` |
| Right notification panel | `Workspace/RouteMap/Panels/NotificationsPanelView.axaml` |
| Definition and seed | `Workspace/RouteMap/Models/RouteMapDefinition.cs`, `RouteMapSeed.cs` |
| Runtime mapping | `Workspace/RouteMap/Services/RouteMapRuntimeMapper.cs` |
| Settings dialog | `Workspace/RouteMap/Settings/*` |
| JSON configuration | `Workspace/RouteMap/Configuration/*` |

## Definition And Schema

The current user profile schema is version `11`:

```json
{
  "schemaVersion": 11,
  "map": {},
  "topBar": {},
  "chains": [],
  "nodes": [],
  "segments": [],
  "cards": [],
  "placeholderRules": []
}
```

The profile does not serialize Avalonia types. Colors are strings, enums are strings,
and margins/radii are custom DTOs.

## Objects

- Nodes contain identity, title, coordinates, kind, label placement, menu kind, static
  visibility, style, and bindings.
- Segments connect nodes and support `Straight` and `RoundedElbow90`.
- Long split segments may have `ActiveRouteFragment` bindings named
  `route.<segmentId>.fragment_<n>.active`.
- Equipment cards contain text/status, start/stop commands, equipment parameters,
  styles, and chain/anchor placement.

Segments are not selectable. Do not add segment selection without changing hit testing,
selection marker rendering, and tests.

## Runtime State

`RouteMapRuntimeMapper` maps signals to runtime objects. State priority is:

```text
Offline -> Fault -> ActiveRoute -> static fallback
```

PLC-mapped `system.fault=true` forces all RouteMap runtime objects into the same `Fault`
visual state as a local `Fault=true`; `Offline` and bad quality keep higher priority.

`Visible=false` hides an object. `Fault=true` overrides active highlighting. Bad quality
or stale active signals put the object offline.

System `connection.connected=false` means Modbus is unavailable: the mapper forces all
nodes and segments to `Offline`; cards show `Не в сети` with the muted indicator and
disable commands independently from their status/text binding. `IsTarget` and `IsLoader`
still visually mark selected nodes until those roles are cleared.

## Commands

`Start`, `Stop`, `Emergency`, `Automatic`, `Manual`, `TargetCommand`, and `LoaderCommand`
are toggle/readback commands. UI writes `true` on selection and `false` on clearing or
switching. PLC readback synchronizes final state.

Card `Start` and `Stop` are mutually exclusive: selecting `Start` writes
`StopCommand=false` before `StartCommand=true`; selecting `Stop` writes
`StartCommand=false` before `StopCommand=true`. If readback returns both command bits
`true`, UI shows only `Stop` as checked.

`StartOffFeedback` and `StopOffFeedback` are active card-only `Read/Bool` roles.
`true` disables the matching button and visually resets `IsChecked=false` without writing
back to PLC. Other `*OffFeedback` roles and `State` remain legacy.

The `Н` button in the card header opens a modal equipment-parameters dialog. Parameters
are stored on the card as `EquipmentParameter`: `Title`, `SignalId`, `Direction`, and
`ValueType`. On open, `Read` and `ReadWrite` parameters use the latest good runtime
snapshot values; `Write` parameters start empty. Bool parameters use a `Вкл/Выкл`
switch; numeric and string parameters are validated by `ValueType`, including `WORD`,
`DWORD`, and `DATE`. Admin mode shows the technical `SignalId • Type` caption; user mode
hides it. `Сохранить` checks the Modbus mapping for each `SignalId` before dispatch,
sends `Write`/`ReadWrite` rows through the normal `SignalWriteRequest` path, and does
not close the dialog.

## Right Panel

RouteMap shows `NotificationsPanelView` on the right: the `Уведомления` tab keeps a
minimum width of `400`, while opening `История` lets the right column grow to the journal
table width. The `Уведомления` tab displays alarms from `Modbus.AlarmMap` after the
dialog is shown: one row per `AlarmMap.Id`, with the timestamp taken from the
`Alarm=false -> true` edge. The row reuses the dialog visual style, opens the dialog on
click, and its `X` removes the row only when the current alarm bit is already `false`.
The bottom `Очистить список` button applies the same rule to all rows, leaving active
alarms visible.

The `История` tab is an in-memory session journal. It records successful sent SignalId
commands, first and changed received values from `Modbus.DataMap`, and alarm activation,
clear, and `OK` events. Direction is shown with arrows: `↓` for received/incoming events
and `↑` for sent commands and `OK`. `Roles` and `Objects` are rendered as one
`Роли / объекты` column, matching `SignalId ↔ Modbus`; `Адрес` shows only the zero-based
offset without the area name. Operator alarms do not become SignalIds and do not move
into `Modbus.DataMap`.

## Editor

`НАСТРОЙКИ` opens `RouteMapSettingsDialog`. In `Application.WorkMode=user`, the button is
hidden and Workspace shows only RouteMap. `ПРИМЕНИТЬ` validates and publishes the
definition without writing the file. `СОХРАНИТЬ` validates, atomically saves JSON, then
publishes the definition. Invalid documents are not published.

Hiding settings in user mode does not create a separate configuration. User mode still
reads the shared `RouteMapRuntime`, `Modbus`, and `ModbusDemo` sections while keeping the
configuration tabs out of the UI, including `Менеджер тревог`. User mode still reacts to
saved `Modbus.AlarmMap` alarm definitions.
In that map, `Alarm area/Offset/Bit` defines the dialog input bit,
`OK area/Offset/Bit` defines the separate acknowledgement bit, `Repeat ms` controls
repetition while the alarm bit stays active, and `Pulse ms` controls the acknowledgement
pulse duration.

The `Карточки` tab lets admins configure equipment parameters: add/remove rows, edit the
title, the single allowed role `EquipmentParameter`, `SignalId`, `Direction`, and
`ValueType`. These parameters remain domain `SignalId` values and automatically appear in
`SignalId ↔ Modbus`; physical addresses are configured only there through
`Modbus.DataMap`. New parameters default to `Word`; old `UInt16` configs remain valid
and compatible.

Validation covers schema version, ID uniqueness, references, binding roles, required
commands, card parameters, geometry, colors, fragment bindings, placeholder rules, and
toggle semantics.
