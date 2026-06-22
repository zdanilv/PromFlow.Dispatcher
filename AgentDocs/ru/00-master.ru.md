# Master: контекст RouteMap/Modbus для агентов

Этот каталог хранит рабочий контекст для агентов, которые меняют `PromFlow.Dispatcher`.
Читайте документы из `AgentDocs/ru` как актуальную краткую карту архитектуры и правил.
Файлы в `AgentDocs/source/ru` — перенесенные исходные гайды с более подробными деталями.

## Что читать

| Задача | Основные документы | Дополнительно |
|---|---|---|
| Меняю RouteMap UI | `02-route-map-guide.ru.md`, `05-coding-rules.ru.md` | `source/ru/route_map_programmer_guide.ru.md` |
| Подключаю PLC или Modbus TCP | `03-modbus-tcp-guide.ru.md`, `04-signal-id-guide.ru.md` | `source/ru/route_map_modbus_tcp_full_guide.ru.md` |
| Добавляю новый сигнал | `04-signal-id-guide.ru.md`, `03-modbus-tcp-guide.ru.md` | `source/ru/signal_id_modbus_tcp_mapping_guide.ru.md` |
| Чиню runtime, DI или lifecycle | `01-architecture-overview.ru.md`, `05-coding-rules.ru.md` | `source/ru/modbus_tcp_integration_guide.ru.md` |
| Проверяю перед PR или commit | `06-testing-and-diagnostics.ru.md` | профильные source-гайды |

## Актуальные документы

- `01-architecture-overview.ru.md` — общий поток данных, Workspace, DI, RouteMap, SignalId, Modbus runtime.
- `02-route-map-guide.ru.md` — RouteMap definition, schema v10, редактор, миграции, validation, runtime state.
- `03-modbus-tcp-guide.ru.md` — общий TCP runtime, `ModbusDemo`, `Modbus.DataMap`, snapshots, запись команд.
- `04-signal-id-guide.ru.md` — правила SignalId, роли, направления, типы и mapping.
- `05-coding-rules.ru.md` — правила разработки с учетом текущей архитектуры.
- `06-testing-and-diagnostics.ru.md` — команды проверки, диагностика и production checklist.

## Исходные гайды

- `source/ru/signal_id_modbus_tcp_mapping_guide.ru.md` — подробное описание SignalId и вкладки `SignalId ↔ Modbus`.
- `source/ru/route_map_programmer_guide.ru.md` — подробности RouteMap UI, geometry, editor и manager.
- `source/ru/route_map_modbus_tcp_full_guide.ru.md` — полный объединенный гайд по RouteMap и Modbus TCP.
- `source/ru/modbus_tcp_integration_guide.ru.md` — краткая интеграционная схема Modbus TCP.
- `source/ru/modbus_demo_controls.ru.md` — как добавлять demo controls и точки `ModbusDemo.DataMap`.
- `source/ru/modbus_demo_description.ru.md` — устройство экрана `Modbus Demo`.
- `source/ru/route_map_signal_binding_roles.ru.md` — справочник ролей `SignalBindingRole`.
- `source/ru/promflow_dispatcher_route_map_ui_merge_recommendations.ru.md` — исторический контекст слияния RouteMap UI.

## Главные инварианты

- RouteMap UI работает только с доменными `SignalId`, не с Modbus-адресами.
- Физическая адресация PLC живет в `Modbus.DataMap`; `ModbusDemo.DataMap` не смешивается с RouteMap.
- `ModbusDemo` владеет TCP endpoint и lifecycle общего runtime.
- `RouteMapConfigurationManager` владеет актуальной definition; не регистрируйте `RouteMapDefinition` как immutable singleton.
- UI не обновляется напрямую из Modbus callback: поток идет через provider, mapper и ViewModel.
- `ПУСК`, `СТОП`, `АВАРИЯ`, loader/target и modes — toggle/readback-команды.
- Legacy `State` и `*OffFeedback` не возвращаются в актуальное поведение.

