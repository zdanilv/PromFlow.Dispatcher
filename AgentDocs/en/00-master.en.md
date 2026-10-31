# PromFlow.Dispatcher documentation map

This is a navigation index for developers and Codex, not a technical contract. Choose one primary guide; use the [code map (RU)](../ru/11-code-map.ru.md) for exact source files and related tests. [AGENTS.md](../../AGENTS.md) governs changes; current branch code and tests establish actual behavior.

## Task → first document

English guides exist for `01`–`07`. Guides `08`–`11` currently exist **only in Russian** and are linked explicitly below. This index does not imply complete English coverage; the [Russian master](../ru/00-master.ru.md) is the primary documentation router.

| Task | First document | Source / test starting point |
|---|---|---|
| Layers, Boot, DI, Workspace, admin/user | [01 — architecture (EN)](01-architecture-overview.en.md) | [Program.cs](../../Configurator.Boot/Program.cs), [WorkspaceViewModelTests](../../Configurator.Tests.Unit/WorkspaceViewModelTests.cs) |
| Route Map, geometry, cards, TopBar, JSON and migrations | [02 — Route Map (EN)](02-route-map-guide.en.md) | [RouteMap](../../Configurator.Desktop/Workspace/RouteMap/), [RouteMapConfigurationTests](../../Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs); UI tests via the code map |
| Modbus TCP, lifecycle, RMW, readback, alarms and ACK | [03 — Modbus (EN)](03-modbus-tcp-guide.en.md) | [Infrastructure.Modbus](../../Configurator.Infrastructure.Modbus/), [ModbusTcpServiceTests](../../Configurator.Infrastructure.Modbus.Tests/ModbusTcpServiceTests.cs), [ModbusAlarmMonitorTests](../../Configurator.Tests.Unit/ModbusAlarmMonitorTests.cs) |
| SignalId, roles, Modbus mapping | [04 — SignalId (EN)](04-signal-id-guide.en.md) | [SignalMapping](../../Configurator.Desktop/Workspace/RouteMap/SignalMapping/), [RouteMapSignalRuntimeAndMappingTests](../../Configurator.Tests.Unit/RouteMap/RouteMapSignalRuntimeAndMappingTests.cs) |
| C#, ReactiveUI, UI threading, resources and file visibility | [05 — coding rules (EN)](05-coding-rules.en.md) | Affected subsystem's entry file and tests via [11 (RU)](../ru/11-code-map.ru.md) |
| Tests, diagnostics, pre-PR checks | [06 — testing and diagnostics (EN)](06-testing-and-diagnostics.en.md) | Select a focused test via [11 (RU)](../ru/11-code-map.ru.md); commands in the guide |
| Material Icons / SVG | [07 — icons (EN)](07-svg-icons-guide.en.md) | [App.axaml](../../Configurator.Desktop/App.axaml), [Assets/icons](../../Configurator.Desktop/Assets/icons/); headless/visual checks via [11 (RU)](../ru/11-code-map.ru.md) |
| Windows / ALT Linux, publishing, EXE/RPM | [08 — build and install (RU)](../ru/08-build-and-install.ru.md) | [Packaging](../../Configurator.Boot/Packaging/); build, full tests and platform smoke per guide |
| Defaults, user files, migration and backup | [09 — configuration locations (RU)](../ru/09-configuration-file-locations.ru.md) | [ApplicationConfigPaths](../../Configurator.Infrastructure/Services/ApplicationConfigPaths.cs), [AppConfigServiceTests](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.cs) |
| OPC UA client/server/runtime, browsing, tags and security limits | [10 — OPC UA (RU)](../ru/10-opc-ua-guide.ru.md) | [Infrastructure.OpcUa](../../Configurator.Infrastructure.OpcUa/), [OpcUaRuntimeTests](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaRuntimeTests.cs), [OpcUaIntegrationTests](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaIntegrationTests.cs) |
| Find feature ownership and the minimum source/test set | [11 — code map (RU)](../ru/11-code-map.ru.md) | Routes to exact files, types and coverage boundaries |

## Current names and factual sources

- User-facing tabs in [WorkspaceView.axaml](../../Configurator.Desktop/Workspace/WorkspaceView.axaml): `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог`, `Modbus TCP`. User mode shows only Route Map; hiding admin tabs does not disable configured Modbus runtime/alarm monitoring.
- `ModbusDemo`, `ModbusDemoViewModel` and `ModbusDemo.DataMap` are internal identifiers, not the current tab title. The shared TCP runtime takes endpoint/lifecycle settings from `ModbusDemo`; RouteMap mapping uses `Modbus.DataMap`, alarms use `Modbus.AlarmMap`. See [03](03-modbus-tcp-guide.en.md).
- The Route Map JSON version is defined by [RouteMapConfigurationDocument.CurrentSchemaVersion](../../Configurator.Desktop/Workspace/RouteMap/Configuration/RouteMapConfigurationDocument.cs); see [02](02-route-map-guide.en.md) for migrations and validation. No independent version number is maintained here.
- OPC UA code and tests exist, but Workspace exposes no OPC UA tab. See [10 (RU)](../ru/10-opc-ua-guide.ru.md) for integration status and limitations.
- Live PLC or external industrial endpoint checks require explicit authorization and an established environment; prefer mocks/loopback. UI is not a safety interlock.

For a discrepancy between language versions, verify current code and the relevant test. English availability does not guarantee that every statement in an older translation is current.

## Historical and extended references (Russian)

[source/ru](../source/ru/) contains historical and/or extended references, **not current contracts**. Read them after the current guide only for old implementation details, migration rationale, comparisons with earlier behavior or porting history. Do not load them by default. Current branch code and tests take priority; historical schema versions and test counts do not describe the current state.

| Reference | Purpose / when to consult |
|---|---|
| [signal_id_modbus_tcp_mapping_guide](../source/ru/signal_id_modbus_tcp_mapping_guide.ru.md) | Extended addressing and older mapping UI examples; compare former bindings after [04](04-signal-id-guide.en.md)/[03](03-modbus-tcp-guide.en.md). |
| [route_map_programmer_guide](../source/ru/route_map_programmer_guide.ru.md) | RouteMap UI, geometry and editor details; investigate older layout/editor behavior after [02](02-route-map-guide.en.md). |
| [route_map_modbus_tcp_full_guide](../source/ru/route_map_modbus_tcp_full_guide.ru.md) | Combined description from `3-route-map-merge`; compare earlier integration rather than infer current contracts. |
| [modbus_tcp_integration_guide](../source/ru/modbus_tcp_integration_guide.ru.md) | Extended SignalId → shared TCP runtime flow; integration history after [03](03-modbus-tcp-guide.en.md). |
| [modbus_demo_controls](../source/ru/modbus_demo_controls.ru.md) | Legacy/demo controls and `ModbusDemo.DataMap`; tasks specifically concerning those controls. |
| [modbus_demo_description](../source/ru/modbus_demo_description.ru.md) | Earlier `Modbus Demo` screen internals; compare old bindings with today's `Modbus TCP` UI. |
| [route_map_signal_binding_roles](../source/ru/route_map_signal_binding_roles.ru.md) | Extended role reference; compare role history after [04](04-signal-id-guide.en.md), checking `SignalBinding.cs`. |
| [promflow_dispatcher_route_map_ui_merge_recommendations](../source/ru/promflow_dispatcher_route_map_ui_merge_recommendations.ru.md) | Porting history from another repository; not instructions for the current runtime. |

**Known hazardous discrepancy:** `promflow_dispatcher_route_map_ui_merge_recommendations.ru.md`, section “Конфигурация Режима”, describes an independent `ModbusDemo` and autostart through `Modbus.AutostartOnWorkspaceOpen`. Current [Modbus DI](../../Configurator.Infrastructure.Modbus/DependencyInjection.cs) builds a shared runtime, and [WorkspaceViewModel](../../Configurator.Desktop/Workspace/WorkspaceViewModel.cs) reads `IModbusDemoOptionsProvider` (`ModbusDemo`). Do not apply that old lifecycle/autostart advice to working configuration. The old `Modbus Demo` tab title in source references also needs checking against XAML. Historical files remain unchanged.
