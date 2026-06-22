# RouteMap Guide

RouteMap is the first Workspace tab and the operator route diagram. It stores topology,
visual settings, and bindings in the RouteMap definition, but it does not store physical
Modbus addresses.

## Main Areas

| Area | Location |
|---|---|
| Screen | `Configurator.Desktop/Workspace/RouteMap/RouteMapDashboardView.axaml` |
| Map drawing | `Workspace/RouteMap/Controls/RouteMapControl.cs` |
| Segment geometry | `Workspace/RouteMap/Controls/RouteSegmentGeometry.cs` |
| Attached cards | `Workspace/RouteMap/Panels/RouteMapAttachedCardsLayer.cs` |
| Definition and seed | `Workspace/RouteMap/Models/RouteMapDefinition.cs`, `RouteMapSeed.cs` |
| Runtime mapping | `Workspace/RouteMap/Services/RouteMapRuntimeMapper.cs` |
| Settings dialog | `Workspace/RouteMap/Settings/*` |
| JSON configuration | `Workspace/RouteMap/Configuration/*` |

## Definition And Schema

The current user profile schema is version `10`:

```json
{
  "schemaVersion": 10,
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
- Equipment cards contain text/status, start/stop commands, styles, and chain/anchor
  placement.

Segments are not selectable. Do not add segment selection without changing hit testing,
selection marker rendering, and tests.

## Runtime State

`RouteMapRuntimeMapper` maps signals to runtime objects. State priority is:

```text
Offline -> Fault -> ActiveRoute -> static fallback
```

`Visible=false` hides an object. `Fault=true` overrides active highlighting. Bad quality
or stale active signals put the object offline.

## Commands

`Start`, `Stop`, `Emergency`, `Automatic`, `Manual`, `TargetCommand`, and `LoaderCommand`
are toggle/readback commands. UI writes `true` on selection and `false` on clearing or
switching. PLC readback synchronizes final state.

Legacy `Momentary`, `State`, and `*OffFeedback` are compatibility/migration concepts only.
Do not restore them as current behavior.

## Editor

`НАСТРОЙКИ` opens `RouteMapSettingsDialog`. `ПРИМЕНИТЬ` validates and publishes the
definition without writing the file. `СОХРАНИТЬ` validates, atomically saves JSON, then
publishes the definition. Invalid documents are not published.

Validation covers schema version, ID uniqueness, references, binding roles, required
commands, geometry, colors, fragment bindings, placeholder rules, and toggle semantics.

