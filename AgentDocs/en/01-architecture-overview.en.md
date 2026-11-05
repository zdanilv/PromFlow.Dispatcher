# Architecture Overview

`PromFlow.Dispatcher` is an Avalonia desktop application with Domain, Application,
Infrastructure, Desktop, and Boot projects. Application references Domain; Desktop and
the infrastructure projects reference Application; Boot composes them.

| Layer | Current responsibility |
|---|---|
| Domain | `Configurator.Domain` currently has a project file but no C# models. `SignalId` is a domain name for a signal, not a type implemented in that project. |
| Application | Signal, Modbus, and OPC UA contracts, options, and validators independent of Desktop and a specific transport. |
| Infrastructure | Shared configuration and OS services in `Configurator.Infrastructure`; TCP client/server, shared runtime, and facades in `Configurator.Infrastructure.Modbus`; OPC UA SDK, client/server/browser/runtime in `Configurator.Infrastructure.OpcUa`. |
| Desktop | Avalonia/ReactiveUI Workspace, RouteMap definition, editors, ViewModels, UI state projection, and session journal. |
| Boot | `Program.cs` reads launch configuration and the user overlay, registers dependencies, and starts the application. |

Use the [code map (RU)](../ru/11-code-map.ru.md) to find a specific owner and test.

## Workspace

`Application.WorkMode` is a launch-shell mode, not an equipment runtime setting. It is
read from the exe-local `Configurator.Boot/appsettings.json`. Mutable runtime sections
`RouteMapRuntime`, `Modbus`, `ModbusDemo`, `OpcUa`, `Help`, and `Startup` are overlaid from the shared writable
`%LOCALAPPDATA%\Configurator\appsettings.json`, so admin and user launches use the same
RouteMap source, SignalId mapping, and Modbus TCP settings.

`Configurator.Boot/appsettings.json` contains `Application.WorkMode`:

- `admin` shows the `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог`, and `Modbus TCP` tabs;
- `user` shows only `Route Map` across the Workspace area, without tabs.

Missing or invalid values are treated as `admin`. In admin mode, `WorkspaceView` has
four tabs:

```text
Route Map
SignalId ↔ Modbus
Менеджер тревог
Modbus TCP
```

`WorkspaceViewModel` owns `RouteMapDashboardViewModel`,
`NotificationsPanelViewModel`, `RouteMapSignalMappingViewModel`, `AlarmManagerViewModel`,
and `ModbusDemoViewModel` (the internal name for the `Modbus TCP` screen).
It may autostart the shared Modbus runtime using `ModbusDemo.AutostartOnWorkspaceOpen`
and `StartupMode`.

In user mode the `SignalId ↔ Modbus`, `Менеджер тревог`, and `Modbus TCP` tabs are
hidden. The `Менеджер тревог` tab is admin-only, but `ModbusAlarmMonitor` runs in both
Workspace modes, reads saved `Modbus.AlarmMap`, and shows dialogs on alarm-bit rising
edges.
An AlarmMap entry contains `Enabled`, `Id`, `Kind`, `Message`, the input `Alarm` bit,
the separate `Acknowledgement` bit, `RepeatIntervalMs`, and
`AcknowledgementPulseDurationMs`; the UI exposes them as `Вкл.`, `Тип`, `Сообщение`,
`Alarm area/Offset/Bit`, `OK area/Offset/Bit`, `Repeat ms`, and `Pulse ms`.
`Kind` can be `Fault`, `Confirmation`, or `Message`; it changes dialog styling while
alarm/ack/repeat behavior stays shared.
After a dialog is shown, the alarm appears in the RouteMap right panel `Уведомления`
tab. `Хорошо` clears the unread marker; the row `X` removes it only after `Alarm=false`.
`Очистить список` bulk-removes only rows that pass the same close check. The `История`
tab is an in-memory session log for successful sent SignalIds, first and changed received
values from `Modbus.DataMap`, and alarm events from `Modbus.AlarmMap`. It expands the
right panel to the table width, uses arrows for direction, and shows the address as a
numeric offset without the Modbus area name.

## Read Flow

The diagram shows the `Modbus` source. `RouteMapSignalRuntime` can instead select
`MockSignalProvider`.

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
`RouteMapDashboardViewModel` separately records snapshots in `RouteMapSessionJournal`
after dispatching to the UI thread; the journal supplies session history.

## Write Flow

This is the `Modbus` write path. With the `Mock` source, the switchable runtime uses
`MockEquipmentCommandDispatcher` without a TCP write.

```text
TopBar / node menu / equipment card / card parameter dialog
  -> SignalWriteRequest
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService.SetAsync
  -> shared Modbus client or local server
  -> poll/readback
  -> RouteMapDashboardViewModel / Avalonia UI
```

The UI may apply optimistic checked state, but readback is the source of truth. Incoming
`ReadWrite` values must not trigger another write.
After a successful `DispatchAsync`, the switchable `RouteMapSignalRuntime` separately
records the sent signal in `RouteMapSessionJournal`.
For the bucket card, momentary Start requests only `StartCommand=true`; the
dispatcher completes its bounded Pulse reset. Stop independently writes Latched
`StopCommand=true/false`; neither action changes the other bit. Runtime readback and
`StartOffFeedback`/`StopOffFeedback` update UI without writing back to PLC.
The card-parameters dialog opened by `Н` uses the same `IEquipmentCommandDispatcher`:
`Write` and `ReadWrite` rows are sent as `SignalWriteRequest`, while `Read` rows only
display the latest good value.

## Ownership

- `Configurator.Boot/Program.cs` assembles desktop DI and the switchable `IRouteMapSignalRuntime`.
- `Configurator.Infrastructure.Modbus/DependencyInjection.cs` registers the shared runtime,
  RouteMap facade, and demo facade.
- `RouteMapConfigurationManager` owns `CurrentDocument` and `CurrentDefinition`.
- `RouteMapConfigurationStorage`, `Migrator`, `Validator`, and `Mapper` handle JSON
  loading, migration, validation, and mapping.
- `RouteMapSessionJournal` owns the in-memory notification and session-history state.
  `NoopSessionJournalExporter` is called during shutdown as the future database export
  hook; the database itself is not implemented yet.
- `EquipmentCardParametersDialogService` owns the modal card-parameter dialog. Parameter
  definitions live in RouteMap, while PLC addresses still live only in `Modbus.DataMap`.

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

## OPC UA: implementation and availability

Application holds OPC UA contracts; `Configurator.Infrastructure.OpcUa` implements the
client, server, browser, and runtime. Boot calls `AddOpcUaInfrastructure`, which registers
services but does not start OPC UA roles. Desktop contains `OpcUaViewModel`, `OpcUaView`,
and tag dialogs. The current `WorkspaceView.axaml` has no OPC UA tab, and
`WorkspaceViewModel` neither receives the OPC UA runtime nor applies
`OpcUa.AutostartOnWorkspaceOpen`. The OPC UA setup screen is therefore not accessible
through the current Workspace.

The OPC UA test project covers options, runtime, tag validation, and a local client/server
browse, read, subscribe, and write scenario. It does not establish readiness for an
external industrial endpoint. DI currently uses `DisabledOpcUaSecurityProvider` and
`AnonymousOpcUaIdentityProvider` with None/Anonymous defaults; this is not a secure
production connection. See the [OPC UA guide (RU)](../ru/10-opc-ua-guide.ru.md) for
contracts, lifecycle limits, and security details.

## Safety Boundary

The UI is not a functional safety layer. Interlocks, emergency behavior, actuator
permissions, and final command acceptance must remain in the PLC.
`Application.WorkMode` controls interface visibility, not PLC command authorization.

