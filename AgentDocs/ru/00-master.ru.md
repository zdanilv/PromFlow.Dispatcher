# Навигация по PromFlow.Dispatcher

Это указатель для разработчика и Codex, а не технический контракт. Выберите один основной гайд; точные исходники и связанные тесты найдите в [11 — карте кода](11-code-map.ru.md). Правила изменений — в [AGENTS.md](../../AGENTS.md); фактическое поведение подтверждают код и тесты текущей ветки.

## Тип задачи → какой документ читать

| Тип задачи | Первый документ | Начальная точка кода / тестов |
|---|---|---|
| Слои, Boot, DI, Workspace, admin/user | [01 — архитектура](01-architecture-overview.ru.md) | [Program.cs](../../Configurator.Boot/Program.cs), [WorkspaceViewModelTests](../../Configurator.Tests.Unit/WorkspaceViewModelTests.cs) |
| Route Map, геометрия, карточки, TopBar, JSON и миграции | [02 — Route Map](02-route-map-guide.ru.md) | [RouteMap](../../Configurator.Desktop/Workspace/RouteMap/), [RouteMapConfigurationTests](../../Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs); UI-тесты — по карте кода |
| Modbus TCP, lifecycle, RMW, readback, тревоги и ACK | [03 — Modbus](03-modbus-tcp-guide.ru.md) | [Infrastructure.Modbus](../../Configurator.Infrastructure.Modbus/), [ModbusTcpServiceTests](../../Configurator.Infrastructure.Modbus.Tests/ModbusTcpServiceTests.cs), [ModbusAlarmMonitorTests](../../Configurator.Tests.Unit/ModbusAlarmMonitorTests.cs) |
| SignalId, роли, привязка к Modbus | [04 — SignalId](04-signal-id-guide.ru.md) | [SignalMapping](../../Configurator.Desktop/Workspace/RouteMap/SignalMapping/), [RouteMapSignalRuntimeAndMappingTests](../../Configurator.Tests.Unit/RouteMap/RouteMapSignalRuntimeAndMappingTests.cs) |
| Соглашения C#, ReactiveUI, потоки UI, ресурсы и видимость файлов | [05 — правила кода](05-coding-rules.ru.md) | Начальный файл и тест затрагиваемой подсистемы — по [11](11-code-map.ru.md) |
| Тесты, диагностика, проверка перед PR | [06 — тестирование и диагностика](06-testing-and-diagnostics.ru.md) | Профильный тест — по [11](11-code-map.ru.md); команды проверки — в гайде |
| Material Icons / SVG | [07 — иконки](07-svg-icons-guide.ru.md) | [App.axaml](../../Configurator.Desktop/App.axaml), [Assets/icons](../../Configurator.Desktop/Assets/icons/); headless/visual checks — по [11](11-code-map.ru.md) |
| Windows / ALT Linux, публикация, EXE/RPM | [08 — сборка и установка](08-build-and-install.ru.md) | [Packaging](../../Configurator.Boot/Packaging/); build, полные тесты и platform smoke по гайду |
| Defaults, пользовательские файлы, перенос и резервная копия | [09 — пути конфигурации](09-configuration-file-locations.ru.md) | [ApplicationConfigPaths](../../Configurator.Infrastructure/Services/ApplicationConfigPaths.cs), [AppConfigServiceTests](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.cs) |
| OPC UA client/server/runtime, browsing, tags и ограничения security | [10 — OPC UA](10-opc-ua-guide.ru.md) | [Infrastructure.OpcUa](../../Configurator.Infrastructure.OpcUa/), [OpcUaRuntimeTests](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaRuntimeTests.cs), [OpcUaIntegrationTests](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaIntegrationTests.cs) |
| Найти реализацию функции и минимальный набор исходников/тестов | [11 — карта кода](11-code-map.ru.md) | Таблица маршрутов с точными файлами, типами и границами покрытия |

## Текущие названия и источники фактов

- Пользовательские вкладки в [WorkspaceView.axaml](../../Configurator.Desktop/Workspace/WorkspaceView.axaml): `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог`, `Modbus TCP`. В user-режиме показывается только Route Map; скрытие admin-вкладок не выключает настроенный Modbus runtime/монитор тревог.
- `ModbusDemo`, `ModbusDemoViewModel`, `ModbusDemo.DataMap` — внутренние идентификаторы, а не текущее название вкладки. Общий TCP runtime использует endpoint/lifecycle из `ModbusDemo`; RouteMap mapping — `Modbus.DataMap`, тревоги — `Modbus.AlarmMap`. Подробности — в [03](03-modbus-tcp-guide.ru.md).
- Версию Route Map JSON определяет [RouteMapConfigurationDocument.CurrentSchemaVersion](../../Configurator.Desktop/Workspace/RouteMap/Configuration/RouteMapConfigurationDocument.cs), миграции и проверки — [02](02-route-map-guide.ru.md). Отдельное число версии здесь не дублируется.
- OPC UA-код и тесты существуют, но отдельной OPC UA-вкладки в Workspace нет. Фактическая интеграция и ограничения — в [10](10-opc-ua-guide.ru.md).
- Проверки с PLC или внешним промышленным endpoint выполняются только при явном разрешении и установленной среде; используйте mocks/loopback. UI не является safety interlock.

Английский [master](../en/00-master.en.md) перечисляет доступные переводы `01`–`07`; для `08`–`11` он ссылается на RU. При расхождении языковых версий сверяйте код и профильный тест.

## Исторические и расширенные материалы

[source/ru](../source/ru/) — исторические и/или расширенные справочные источники, **не действующий контракт**. Читайте их после актуального гайда, только если нужны детали старой реализации, причины миграции, сравнение прежнего поведения или контекст переноса. Не включайте их в обычный маршрут чтения. При расхождении приоритет имеют код и тесты текущей ветки; исторические версии схем и количества тестов не описывают текущее состояние.

| Материал | Назначение / когда открывать |
|---|---|
| [signal_id_modbus_tcp_mapping_guide](../source/ru/signal_id_modbus_tcp_mapping_guide.ru.md) | Расширенные примеры адресации и старого mapping UI; для сравнения прежних привязок после [04](04-signal-id-guide.ru.md)/[03](03-modbus-tcp-guide.ru.md). |
| [route_map_programmer_guide](../source/ru/route_map_programmer_guide.ru.md) | Детали RouteMap UI, geometry и editor; для расследования старого layout/редактора после [02](02-route-map-guide.ru.md). |
| [route_map_modbus_tcp_full_guide](../source/ru/route_map_modbus_tcp_full_guide.ru.md) | Объединённое описание эпохи `3-route-map-merge`; для сравнения прежней интеграции, а не текущего контракта. |
| [modbus_tcp_integration_guide](../source/ru/modbus_tcp_integration_guide.ru.md) | Расширенная схема SignalId → общий TCP runtime; при разборе истории интеграции после [03](03-modbus-tcp-guide.ru.md). |
| [modbus_demo_controls](../source/ru/modbus_demo_controls.ru.md) | Устройство legacy/demo-контролов и `ModbusDemo.DataMap`; только задачи именно этих контролов. |
| [modbus_demo_description](../source/ru/modbus_demo_description.ru.md) | Подробности прежнего экрана `Modbus Demo`; для сопоставления старых биндингов с нынешним UI `Modbus TCP`. |
| [route_map_signal_binding_roles](../source/ru/route_map_signal_binding_roles.ru.md) | Расширенный справочник ролей; для сравнения истории ролей после [04](04-signal-id-guide.ru.md), с проверкой `SignalBinding.cs`. |
| [promflow_dispatcher_route_map_ui_merge_recommendations](../source/ru/promflow_dispatcher_route_map_ui_merge_recommendations.ru.md) | История переноса из другого репозитория; не инструкция по текущему runtime. |

**Выявленное опасное расхождение:** в `promflow_dispatcher_route_map_ui_merge_recommendations.ru.md`, раздел «Конфигурация Режима», заявлены независимый `ModbusDemo` и автозапуск через `Modbus.AutostartOnWorkspaceOpen`. Это не текущий контракт: [Modbus DI](../../Configurator.Infrastructure.Modbus/DependencyInjection.cs) собирает общий runtime, а [WorkspaceViewModel](../../Configurator.Desktop/Workspace/WorkspaceViewModel.cs) читает `IModbusDemoOptionsProvider` (`ModbusDemo`). Не переносите старые советы по lifecycle/autostart в рабочую конфигурацию. Старое имя вкладки `Modbus Demo` в source-материалах также требует сверки с XAML. Исторические файлы сохранены без переписывания.
