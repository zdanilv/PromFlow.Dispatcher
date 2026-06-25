# Обзор архитектуры

`PromFlow.Dispatcher` - Avalonia desktop приложение со слоями Boot, Desktop,
Application, Infrastructure, Persistence и Modbus infrastructure. Application задает
contracts и policy. Infrastructure владеет adapters и storage. Desktop владеет
ViewModels, views и lifecycle coordination.

## Startup route

Startup больше не открывает workspace напрямую. `MainViewModel` отправляет пользователя
в administrator bootstrap, если пользователей нет, иначе в login. После успешной
authentication `ApplicationRuntimeCoordinator` запускает runtime services и обновляет
license state до того, как `WorkspaceViewModel` создаст tab content.

## Dynamic workspace

Workspace tabs создаются по descriptors. Каждый `WorkspaceTabDescriptor` содержит id,
header, required permission, optional required license feature, factory и order.
`WorkspaceViewModel` фильтрует descriptors через `IAccessDecisionService` до вызова
factories. Unauthorized ViewModels не создаются.

Текущие product surfaces:

```text
Route Map
SignalId <-> Modbus
Modbus Demo
Archive
License
```

Фактический набор зависит от authenticated role и current license state. Administrator
может открыть License tab для recovery без commercial license, но не обходит licensed
feature gates.

## Lifecycle ownership

`ApplicationRuntimeCoordinator` владеет startup: persistence initialization,
installation identity, license refresh, archive runtime, Modbus archive collector
subscription и Modbus autostart policy. `DesktopShutdownCoordinator` владеет
deterministic shutdown. Workspace ViewModels не запускают и не останавливают archive
runtime или collector.

## Поток чтения

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

RouteMap получает `SignalValue` по `SignalId`, а не raw coils/registers. Mapper
преобразует values, quality и stale state в `RouteMapRuntimeState`.

## Поток записи

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

UI может временно показать optimistic state, но source of truth - readback. Входной
`ReadWrite` сигнал не должен порождать повторную запись.

## Archive и audit

Archive storage находится в Persistence и не зависит от Desktop, Avalonia, ReactiveUI
или Modbus infrastructure. Modbus callbacks enqueue records через application contracts;
они не выполняют SQL. Archive UI использует paged/cancelable queries и декодирует
snapshot details только по запросу.

## Authorization и license

Permissions и license features являются независимыми входами. UI visibility не является
authorization. Service boundaries проверяют role permissions и, где нужно, license
features через `IAccessDecisionService` и `LicenseFeatureGate`.

## Safety boundary

UI не является functional safety layer. Interlocks, emergency behavior, actuator
permissions и final command acceptance должны оставаться в PLC.
