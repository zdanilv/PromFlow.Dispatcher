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

The current user profile uses schema version `22`.

```json
{
  "schemaVersion": 22,
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

The `v18 → v19` migration preserves nodes, segments, coordinates, and bindings.
A `deadEnd` segment connects a regular node to a `terminalAnchor`. Its
`terminalSide` (`from` or `to`) identifies the anchor endpoint. The optional
`terminalCapLength` specifies the cap length in logical units. An anchor belongs
to one segment and has no roles or bindings. Segment bindings apply to its main
track. Cap rendering and editing terminal elements are deferred to later stages.

The `v20 → v21` migration adds `visibility: "user"` to parameters where the field
is absent. It preserves explicit valid visibility values, existing setpoints, and
additional JSON fields. The manager backs up the active profile before saving the
migrated file atomically. Explicit invalid visibility values cause a load or validation
error.

The `v21 → v22` migration adds defaults for missing or null `topBar.unload`,
`rotation`, and `load` sections. It preserves configured commands, `reset`,
`emergency`, and historical `automatic` and `manual`. Migration and round-trip
tests verify that custom labels, SignalIds, optional bindings, and extension
fields on the document, TopBar, button, and binding survive. The manager backs up
the active file before saving a successful migration. Loading a v22 file fills
missing sections in memory without rewriting it. An existing section with a
missing required binding fails validation.

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
disable commands independently from their status/text binding. The `Н` button stays
available in both modes, so editable values can be saved locally without PLC access. `IsTarget` and `IsLoader`
still visually mark selected nodes until those roles are cleared.

## Commands

### TopBar

In `Application.WorkMode=admin`, the default command order is
**ВЫГРУЗКА → ВРАЩЕНИЕ → ЗАГРУЗКА → СБРОС → АВАРИЯ → НАСТРОЙКИ**. The center
group scrolls when space is limited, with «ПРОМ Диспетчер» on the left and
`ПОМОЩЬ` on the right. In `user`, `СБРОС`, `АВАРИЯ`, and `ПОМОЩЬ` remain
visible. The three new commands and `НАСТРОЙКИ` are hidden. `TopBarViewModel`
also guards direct execution of the three commands in `user` mode.
`Application.WorkMode` selects the application mode and gates these commands.
Secure authentication is outside its scope. The PLC owns equipment permissives
and interlocks.

| Default label | `topBar` section | C# role | JSON role | Default SignalId |
|---|---|---|---|---|
| `ВЫГРУЗКА` | `unload` | `UnloadCommand` | `unloadCommand` | `system.command.unload` |
| `ВРАЩЕНИЕ` | `rotation` | `RotationCommand` | `rotationCommand` | `system.command.rotate` |
| `ЗАГРУЗКА` | `load` | `LoadCommand` | `loadCommand` | `system.command.load` |

All three are momentary `Button` controls with a required `ReadWrite/Bool`
binding and a mandatory Modbus `Pulse` mapping. They require fresh, good
`connection.connected=true`, plus fresh, good `true` from `Enabled` when
configured. A pending send blocks reactivation of the same command. A missing
or incompatible point blocks the command in Modbus mode. Snapshots update
availability without dispatch. The JSON fragment in the Russian guide shows the
three new sections and uses `direction: "readWrite"` and `valueType: "bool"`.
[Route Map JSON example](../ru/02-route-map-guide.ru.md#верхняя-панель)

In `НАСТРОЙКИ` → `TopBar`, edit `Текст` and the required command `SignalId`
in `Signal bindings`. The required row stays in the table. `Добавить binding`
creates one optional `Read/Bool` `Enabled` row with initial SignalId
`<commandSignalId>.enabled`. Edit its SignalId or use `Удалить` to remove it.
`ПРИМЕНИТЬ` publishes the validated draft for the current session. `СОХРАНИТЬ`
saves and publishes it. Both refresh TopBar and the inventory without sending
these commands or assigning physical addresses. `ЗАКРЫТЬ` discards edits still
in the draft.

### Equipment cards and existing commands

The bucket card uses `equip.bucket.power` as a read-only `PowerFeedback` Bool signal.
Its 40×40 PowerStandby icon is red for a good, fresh `false`, green for a good,
fresh `true`, and neutral gray for missing, bad, stale, offline, or invalid data.
Input and snapshots never dispatch a command from this indicator. Its physical
address must be mapped from the PLC contract, never inferred from the old selector.

`Start` is a momentary Button. One activation requests only `StartCommand=true`;
the Modbus Pulse writes `true`, waits the configured duration (300 ms by default),
then makes a bounded attempt to write `false`. It never clears Stop. `Stop` remains
a ToggleButton that writes only `StopCommand=true/false`; good, fresh readback
determines the final checked state, and snapshot updates do not write back.
Dispatch/confirmation errors remain visible. These software controls do not replace
PLC interlocks or a safety stop. `Emergency`, `TargetCommand`, and `LoaderCommand`
retain their existing semantics.
The dispatcher rejects a saved Start mapping that is not Pulse or a Stop mapping
that is not Latched before writing. If fresh Stop readback does not confirm the
request within 10 seconds, Pending clears with a visible error and no automatic retry.

`Automatic` and `Manual` are hidden in user and admin TopBar and settings. Their
commands are inert and their SignalIds are absent from the active mapping inventory.
Historical TopBar JSON fields are preserved for compatibility. `СБРОС` remains
a `system.reset` Pulse command. `АВАРИЯ` keeps its ToggleButton and the default
`system.emergency` Latched mapping.

`StartOffFeedback` and `StopOffFeedback` are active card-only `Read/Bool` roles.
`true` disables the matching button and, for historical Toggle cards, clears checked
without writing back to PLC. Other `*OffFeedback` roles and `State` remain legacy.

The `Н` button in the card header opens a modal equipment-parameters dialog. Each
`EquipmentCardParameter` has a title, a signal binding with the `EquipmentParameter`
role, and a `Visibility` setting. The button stays available regardless of the card `Enabled` binding and
connection state in both modes. Editable parameters also persist a local setpoint,
pending-auto-dispatch flag, and last dispatch error in `route-map.json`. On open, `Read`
uses the latest good runtime snapshot while `Write`/`ReadWrite` use a saved setpoint when
one exists. Bool parameters use a `Вкл/Выкл`
switch; numeric and string parameters are validated by `ValueType`, including `WORD`,
`DWORD`, and `DATE`. Admin mode shows the technical `SignalId • Type` caption; user mode
hides it. Offline `Сохранить` validates and stores editable rows locally without a
Modbus mapping or dispatch. The first Modbus reconnect dispatches each pending value once;
both success and failure clear pending, and failures remain visible until a manual save
retries. Online save uses normal mapping validation and `SignalWriteRequest` dispatch
without closing the dialog.

The `cards[].parameters[].visibility` field controls which rows appear in the
operator dialog for the current `Application.WorkMode`.

| JSON value | Editor label | `user` dialog | `admin` dialog |
|---|---|---|---|
| `user` (`EquipmentParameterVisibility.User`) | Для пользователя | Visible | Visible |
| `admin` (`EquipmentParameterVisibility.Admin`) | Только для администратора | Hidden | Visible |

The dialog filters parameters before creating rows and preserves their order.
`Сохранить` processes visible editable rows. With no visible rows, the dialog
displays «Для текущего режима нет доступных настроек оборудования.», disables
`Сохранить`, and keeps `Закрыть` available.

## Right Panel

RouteMap shows `NotificationsPanelView` on the right: the `Уведомления` tab keeps a
minimum width of `400`, while opening `История` lets the right column grow to the journal
table width. The `Уведомления` tab displays alarms from `Modbus.AlarmMap` after the
dialog is shown: one row per `AlarmMap.Id`, with the timestamp taken from the
`Alarm=false -> true` edge. The row reuses the dialog visual style, opens the dialog on
click, and its `X` removes the row only when the current alarm bit is already `false`.
The bottom `Очистить список` button applies the same rule to all rows, leaving active
alarms visible.

The `История` tab shows administrators the latest 1,000 events from the current session.
The journal records completed SignalId sends, the first and changed valid values from
`Modbus.DataMap`, and alarm activation, clear, and local `OK` events. The table uses `↓`
for received events and `↑` for sent commands and `OK`. `Roles` and `Objects` are rendered as one
`Роли / объекты` column, matching `SignalId ↔ Modbus`; `Адрес` shows only the zero-based
offset without the area name. Operator alarms do not become SignalIds and do not move
into `Modbus.DataMap`.

### History archives

The writer saves incoming events to `history/events.db` in batches of up to 200 events
per transaction. `Настройки приложения` includes the ZIP schedule, destination, retention,
and `Архивировать сейчас` for manual runs. The default schedule is once a day. SQLite
recording continues while the ZIP schedule is disabled.

Each ZIP contains `events.jsonl` and `manifest.json` for a range of committed SQLite IDs.
The service verifies the SHA-256 digest, event count, and ID range before recording the
archive in SQLite. See the [storage guide](../ru/09-configuration-file-locations.ru.md)
for paths, settings, and retention rules.

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
`ValueType`. In «Настройки оборудования», the «Отображать для» column sets each
row's visibility. The editor shows all rows, new parameters default to `user`,
and duplicating a card copies each row's visibility. New parameters default to
`Word`, and existing `UInt16` configs remain valid. Changing `SignalId`, `Direction`,
or `ValueType` clears stored setpoint metadata, and a duplicate starts without it.

Changing only `Visibility` preserves the signal binding, `SavedValue`,
`PendingAutoDispatch`, and `LastDispatchError`. The full definition, the
`SignalId ↔ Modbus` inventory, and `EquipmentParameterAutoDispatcher` use both
visibility categories. Physical addresses are configured in `Modbus.DataMap`, and
the PLC remains the authority for control.

Validation covers schema version, ID uniqueness, references, binding roles, required
commands, card parameters, geometry, colors, fragment bindings, placeholder rules, and
toggle semantics.
