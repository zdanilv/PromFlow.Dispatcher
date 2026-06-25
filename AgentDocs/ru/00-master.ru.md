# Master: контекст PromFlow.Dispatcher для агентов

Этот каталог хранит рабочий контекст для изменений в `PromFlow.Dispatcher`.
Используйте `AgentDocs/ru` как актуальную русскую карту, `AgentDocs/en` как
английскую карту и `AgentDocs/source/ru` как исторические перенесенные guides.

## Что читать

| Задача | Основные документы | Дополнительно |
|---|---|---|
| Менять RouteMap UI | `02-route-map-guide.ru.md`, `05-coding-rules.ru.md` | `source/ru/route_map_programmer_guide.ru.md` |
| Подключать PLC или Modbus TCP | `03-modbus-tcp-guide.ru.md`, `04-signal-id-guide.ru.md` | `source/ru/route_map_modbus_tcp_full_guide.ru.md` |
| Добавлять новый сигнал | `04-signal-id-guide.ru.md`, `03-modbus-tcp-guide.ru.md` | `source/ru/signal_id_modbus_tcp_mapping_guide.ru.md` |
| Чинить runtime, DI или lifecycle | `01-architecture-overview.ru.md`, `10-operations-and-recovery.ru.md` | `source/ru/modbus_tcp_integration_guide.ru.md` |
| Работать с архивом | `07-archive-guide.ru.md`, `06-testing-and-diagnostics.ru.md` | implementation progress |
| Работать с авторизацией | `08-authorization-guide.ru.md`, `05-coding-rules.ru.md` | implementation progress |
| Работать с offline license | `09-offline-license-guide.ru.md`, `08-authorization-guide.ru.md` | implementation progress |
| Использовать приложение после Stage 14 | `11-operator-user-guide.ru.md`, `10-operations-and-recovery.ru.md` | implementation progress |
| Проверять перед PR или commit | `06-testing-and-diagnostics.ru.md`, `10-operations-and-recovery.ru.md` | профильные source-guides |

## Актуальные документы

- `01-architecture-overview.ru.md` - слои, dynamic workspace, authorization, license, archive и lifecycle ownership.
- `02-route-map-guide.ru.md` - RouteMap definition, schema v10, editor, migrations, validation и runtime state.
- `03-modbus-tcp-guide.ru.md` - shared TCP runtime, `ModbusDemo`, `Modbus.DataMap`, snapshots и writes.
- `04-signal-id-guide.ru.md` - SignalId naming, roles, directions, types и mapping.
- `05-coding-rules.ru.md` - правила разработки для текущей архитектуры.
- `06-testing-and-diagnostics.ru.md` - verification commands, diagnostics и Stage 14 acceptance checks.
- `07-archive-guide.ru.md` - archive runtime, query, health, export, backup и retention operations.
- `08-authorization-guide.ru.md` - login, session revocation, permissions и recovery authorization.
- `09-offline-license-guide.ru.md` - offline license verification, installation и feature policy.
- `10-operations-and-recovery.ru.md` - lifecycle operations, recovery checklist и manual endurance runbook.
- `11-operator-user-guide.ru.md` - сквозное руководство оператора: bootstrap, users, license, RouteMap, archive и troubleshooting.

## Главные инварианты

- RouteMap UI работает с доменными `SignalId`, а не с Modbus addresses.
- Физическая адресация PLC живет в `Modbus.DataMap`; не смешивать ее с `ModbusDemo.DataMap`.
- `ModbusDemo` владеет настройками TCP endpoint; centralized lifecycle владеет startup/shutdown runtime.
- `RouteMapConfigurationManager` владеет active definition; не регистрировать `RouteMapDefinition` как immutable singleton.
- Modbus callbacks не обновляют Avalonia UI напрямую; поток идет через provider, mapper и ViewModel.
- UI visibility не является authorization. Permissions и license features проверяются на service boundary.
- User role и product license независимы; Administrator не обходит commercial features.
- Archive code не зависит от Avalonia, ReactiveUI или ViewModels.
- Timestamps хранятся в UTC, queues bounded, shutdown deterministic.
- Emergency command delivery не блокируется недоступностью архива.
- Safety interlocks, emergency behavior и final command acceptance остаются в PLC.
