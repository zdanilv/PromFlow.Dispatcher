# Architecture Overview

`PromFlow.Dispatcher` is an Avalonia desktop application split into Boot, Desktop,
Application, and Infrastructure layers. RouteMap is implemented in the desktop layer,
while signal contracts and Modbus runtime integration are exposed through application and
infrastructure services.

## Workspace

The current `WorkspaceView` has three tabs:

```text
Route Map
SignalId ↔ Modbus
Modbus Demo
```

`WorkspaceViewModel` owns `RouteMapDashboardViewModel`,
`RouteMapSignalMappingViewModel`, and `ModbusDemoViewModel`. It may autostart the shared
Modbus runtime using `ModbusDemo.AutostartOnWorkspaceOpen` and `StartupMode`.

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
```

Do not move endpoint or lifecycle ownership into RouteMap.

## Safety Boundary

The UI is not a functional safety layer. Interlocks, emergency behavior, actuator
permissions, and final command acceptance must remain in the PLC.

