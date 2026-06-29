# Architecture Overview

`PromFlow.Dispatcher` is an Avalonia desktop application split into Boot, Desktop,
Application, and Infrastructure layers. RouteMap is implemented in the desktop layer,
while signal contracts and Modbus runtime integration are exposed through application and
infrastructure services.

## Workspace

`Application.WorkMode` is a launch-shell mode, not an equipment runtime setting. It is
read from the exe-local `Configurator.Boot/appsettings.json`. Mutable runtime sections
`RouteMapRuntime`, `Modbus`, and `ModbusDemo` are overlaid from the shared writable
`%LOCALAPPDATA%\Configurator\appsettings.json`, so admin and user launches use the same
RouteMap source, SignalId mapping, and Modbus TCP settings.

`Configurator.Boot/appsettings.json` contains `Application.WorkMode`:

- `admin` shows the `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог`, and `Modbus Demo` tabs;
- `user` shows only `Route Map` across the Workspace area, without tabs.

Missing or invalid values are treated as `admin`. In admin mode, `WorkspaceView` has
four tabs:

```text
Route Map
SignalId ↔ Modbus
Менеджер тревог
Modbus Demo
```

`WorkspaceViewModel` owns `RouteMapDashboardViewModel`,
`RouteMapSignalMappingViewModel`, `AlarmManagerViewModel`, and `ModbusDemoViewModel`.
It may autostart the shared Modbus runtime using `ModbusDemo.AutostartOnWorkspaceOpen`
and `StartupMode`.

In user mode the `SignalId ↔ Modbus`, `Менеджер тревог`, and `Modbus Demo` tabs are
hidden. The `Менеджер тревог` tab is admin-only, but `ModbusAlarmMonitor` runs in both
Workspace modes, reads saved `Modbus.AlarmMap`, and shows dialogs on alarm-bit rising
edges.
An AlarmMap entry contains `Enabled`, `Id`, `Kind`, `Message`, the input `Alarm` bit,
the separate `Acknowledgement` bit, `RepeatIntervalMs`, and
`AcknowledgementPulseDurationMs`; the UI exposes them as `Вкл.`, `Тип`, `Сообщение`,
`Alarm area/Offset/Bit`, `OK area/Offset/Bit`, `Repeat ms`, and `Pulse ms`.
`Kind` can be `Fault`, `Confirmation`, or `Message`; it changes dialog styling while
alarm/ack/repeat behavior stays shared.

## Read Flow

```text
ModbusDemo endpoint/lifecycle
  -> shared ModbusRuntimeService
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> Avalonia UI
```

RouteMap receives `SignalValue` objects keyed by `SignalId`, not raw coils or registers.
The mapper converts values, quality, and stale state into `RouteMapRuntimeState`.

## Write Flow

```text
TopBar / node menu / equipment card
  -> SignalWriteRequest
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService.SetAsync
  -> shared Modbus client or local server
  -> poll/readback
```

The UI may apply optimistic checked state, but readback is the source of truth. Incoming
`ReadWrite` values must not trigger another write.
Card `Start` and `Stop` are mutually exclusive: selecting one writes `false` to the
opposite command before writing `true` to the selected command. Runtime readback and
`StartOffFeedback`/`StopOffFeedback` update UI without writing back to PLC.

## Ownership

- `Configurator.Boot/Program.cs` assembles desktop DI and the switchable `IRouteMapSignalRuntime`.
- `Configurator.Infrastructure.Modbus/DependencyInjection.cs` registers the shared runtime,
  RouteMap facade, and demo facade.
- `RouteMapConfigurationManager` owns `CurrentDocument` and `CurrentDefinition`.
- `RouteMapConfigurationStorage`, `Migrator`, `Validator`, and `Mapper` handle JSON
  loading, migration, validation, and mapping.

## Modbus Runtime

`ModbusRuntimeService` coordinates client/server roles. `ModbusDemo` owns endpoint,
UnitId, port, start addresses, autostart, and lifecycle. RouteMap uses the same runtime
through a separate facade and separate data map.

```text
ModbusDemo.DataMap -> demo UI facade
Modbus.DataMap     -> RouteMap facade and SignalId mapping tab
Modbus.AlarmMap    -> alarm dialogs in admin/user and acknowledgement pulses
```

`system.fault` belongs to `Modbus.DataMap` as the PLC-mapped RouteMap global-fault
SignalId. Operator dialogs still belong only to `Modbus.AlarmMap`.

Do not move endpoint or lifecycle ownership into RouteMap.

## Safety Boundary

The UI is not a functional safety layer. Interlocks, emergency behavior, actuator
permissions, and final command acceptance must remain in the PLC.

