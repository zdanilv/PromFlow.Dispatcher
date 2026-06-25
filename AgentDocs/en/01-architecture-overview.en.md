# Architecture Overview

`PromFlow.Dispatcher` is an Avalonia desktop application split into Boot, Desktop,
Application, Infrastructure, Persistence and Modbus infrastructure layers. Application
defines contracts and policy. Infrastructure owns adapters and storage. Desktop owns
ViewModels, views and lifecycle coordination.

## Startup Route

Startup is not a direct jump into workspace. `MainViewModel` routes to administrator
bootstrap when no users exist, otherwise to login. After successful authentication,
`ApplicationRuntimeCoordinator` starts application runtime services and refreshes license
state before `WorkspaceViewModel` creates any tab content.

## Dynamic Workspace

Workspace tabs are descriptor-driven. Each `WorkspaceTabDescriptor` contains an id,
header, required permission, optional required license feature, factory and order.
`WorkspaceViewModel` filters descriptors through `IAccessDecisionService` before invoking
factories. Unauthorized ViewModels are never created.

The current product surfaces are:

```text
Route Map
SignalId <-> Modbus
Modbus Demo
Archive
License
```

The exact visible set depends on the authenticated role and current license state.
Administrator can open the License tab for recovery without a commercial license, but
does not bypass licensed feature gates.

## Lifecycle Ownership

`ApplicationRuntimeCoordinator` owns startup of persistence, installation identity,
license refresh, archive runtime, Modbus archive collector subscription and Modbus
autostart policy. `DesktopShutdownCoordinator` owns deterministic shutdown. Workspace
ViewModels do not start or stop archive runtime or collector.

## Read Flow

```text
ModbusDemo endpoint settings
  -> centralized Modbus runtime lifecycle
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> Avalonia UI
```

RouteMap receives `SignalValue` objects keyed by `SignalId`, not raw coils or registers.
The mapper converts values, quality and stale state into `RouteMapRuntimeState`.

## Write Flow

```text
TopBar / node menu / equipment card
  -> SignalWriteRequest
  -> IAccessDecisionService
  -> RuntimeCommandDeliveryGate
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService.SetAsync
  -> shared Modbus client or local server
  -> poll/readback
```

The UI may show temporary optimistic state, but readback is the source of truth. Incoming
`ReadWrite` values must not trigger another write.

## Archive And Audit

Archive storage is in Persistence and has no dependency on Desktop, Avalonia, ReactiveUI
or Modbus infrastructure. Modbus callbacks enqueue records through application contracts;
they do not execute SQL. Archive UI uses paged/cancelable queries and decodes snapshot
details only on demand.

## Authorization And License

Permissions and license features are independent inputs. UI visibility is not
authorization. Service boundaries enforce role permissions and, where required, license
features through `IAccessDecisionService` and `LicenseFeatureGate`.

## Safety Boundary

The UI is not a functional safety layer. Interlocks, emergency behavior, actuator
permissions and final command acceptance must remain in the PLC.
