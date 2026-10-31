# Проверка этапов 0–1 документационного плана (`17-demo-v2`)

## Исходная точка до правок

- Рабочая ветка: `17-demo-v2`; `HEAD`: `eaa8f53309dbf8d0fb73673654276f0fca759661`.
- Локальная `16-demo-v1` указывала на тот же SHA; `git diff --stat 16-demo-v1...17-demo-v2` был пуст.
- Рабочее дерево: только пользовательский неотслеживаемый файл `docs/plans/PromFlow_Documentation_Optimization_Implementation_Plan_17-demo-v2.md`. Он не изменялся.
- До правок: `README.md` — 19 723 байта UTF-8, 221 строка; `AGENTS.md` — 19 615 байт, 141 строка; `AgentDocs/ru/00-master.ru.md` — 7 599 байт, 73 строки; `AgentDocs/en/00-master.en.md` — 3 276 байт, 46 строк.
- `AgentDocs`: 26 Markdown-файлов (RU — 10, EN — 8, `source/ru` — 8). Основные целевые документы и ссылки: `README.md`, `AGENTS.md`, `AgentDocs/{ru,en}/00-master.*.md`, `AgentDocs/{ru,en}/01-architecture-overview.*.md`, `AgentDocs/{ru,en}/02-route-map-guide.*.md`, `AgentDocs/ru/08-build-and-install.ru.md`.
- Разбивка usage/tokens клиента не предоставлена: исходные метрики Codex — **нет данных**.

Адресный поиск подтвердил: `CurrentSchemaVersion = 16`; четыре `TabItem Header` в `WorkspaceView.axaml`; регистрацию `AddOpcUaInfrastructure` в `Program.cs`; выбор `WorkMode` в `ApplicationOptions`/`WorkspaceViewModel`; два рецепта `RPM_PATH` перед `su -`. Выявленные расхождения и исходники приведены ниже.

## Матрица сохранения инвариантов `AGENTS.md`

Таблица фиксирует место для полной спецификации перед будущим TASK-11. На этапах 0–1 текст `AGENTS.md` не удалялся. «Дополнить позже» означает, что перенос ещё не подтверждён и сокращать соответствующее правило нельзя.

| Инвариант из `AGENTS.md` | Актуальное место / статус до сокращения |
|---|---|
| Нет live PLC/field-device операций без разрешения | `AGENTS.md`; `03-modbus`, `06-testing` нужно проверить и дополнить перед TASK-11; `10-opc-ua` ещё не создан |
| UI не заменяет PLC interlock | `01-architecture`, README; `03-modbus` проверить перед TASK-11 |
| Route Map использует доменные SignalId | `02-route-map`, `04-signal-id` |
| `Modbus.DataMap`, `Modbus.AlarmMap`, `ModbusDemo.DataMap` раздельны | `03-modbus` |
| `system.fault` принадлежит `Modbus.DataMap` | `03-modbus`, `02-route-map` |
| Текущая схема, миграции и JSON-совместимость | `02-route-map`; версия — `RouteMapConfigurationDocument.CurrentSchemaVersion` |
| `RouteMapConfigurationManager` владеет mutable definition | `01-architecture`, `02-route-map` проверить перед TASK-11 |
| Quality/stale/readback и UI thread; snapshot не пишет в PLC | `02-route-map`, `01-architecture`, `05-coding`; отсутствие обратной записи проверить перед TASK-11 |
| Порядок Start/Stop и OffFeedback | `02-route-map`, `01-architecture` |
| Селектор `С`: две команды и порядок записи | `02-route-map`, `06-testing` |
| `system.reset` — Pulse | `02-route-map`, `03-modbus`, `06-testing` |
| Нет автоповтора неидемпотентных команд | `05-coding`; `03-modbus` дополнить перед TASK-11 |
| Register-bit RMW сохраняет соседние биты | `03-modbus`, `05-coding` |
| Alarm ACK pulse/repeat/clear | `03-modbus`, `06-testing` |
| Offline setpoint, одна автоотправка | `02-route-map`, `06-testing`, `09-configuration` |
| OPC UA security/identity defaults и lifecycle | `AGENTS.md`; полный `10-opc-ua` ещё не создан |
| Профиль пользователя и install defaults | `09-configuration`, `01-architecture` |
| Windows/ALT и видимость в Visual Studio | `08-build`, `05-coding` |
| Пропорциональные тесты и честный отчёт | `06-testing`; правило оставлено в `AGENTS.md` |

## Подтверждение TASK-01…05 исходным кодом

| TASK | Источник | Вывод |
|---|---|---|
| 01 | `Configurator.Desktop/Workspace/RouteMap/Configuration/RouteMapConfigurationDocument.cs`; `AgentDocs/{ru,en}/02-route-map-guide.*.md` | `CurrentSchemaVersion = 16`; профильные гайды описывают v16, master-индексы ссылались на v13/v11 |
| 02 | `Configurator.Desktop/Workspace/WorkspaceView.axaml`, `WorkspaceViewModel.cs` | В UI четыре вкладки: Route Map, SignalId ↔ Modbus, Менеджер тревог, Modbus TCP; `ModbusDemoViewModel` и секция `ModbusDemo` — внутренние имена |
| 03 | `README.md`, `AgentDocs/ru/08-build-and-install.ru.md` до правок | `RPM_PATH` вычислялся до `su -`, а использовался в root-shell без передачи значения |
| 04 | `Configurator.Application/Services/ApplicationOptions.cs`, `Configurator.Desktop/Workspace/WorkspaceView.axaml`, `Configurator.Infrastructure/Services/Authorization/AuthApp.cs`, `Configurator.Desktop/Main/MainViewModel.cs` | `WorkMode` выбирает видимость; демонстрационная аутентификация не защищает Workspace и не является PLC authorization |
| 05 | `Configurator.Application/Services/OpcUa/`, `Configurator.Infrastructure.OpcUa/`, `Configurator.Infrastructure.OpcUa.Tests/`, `Configurator.Boot/Program.cs`, `Configurator.Desktop/Workspace/WorkspaceView.axaml` | Компоненты и локальные тестовые сценарии есть; вкладки OPC UA в Workspace нет; DI регистрирует `DisabledOpcUaSecurityProvider` и `AnonymousOpcUaIdentityProvider` с defaults None/Anonymous |

Тесты продукта и установка RPM в ALT Linux в рамках этих документационных этапов не запускались. Их результат нельзя считать подтверждением поведения на промышленном endpoint. Отдельное руководство OPC UA, сокращение `AGENTS.md` и README, автоматический checker и остальные этапы остаются последующей работой. В частности, строка `Scope: branch 16-demo-v1` в `AGENTS.md` сохраняется до запланированного TASK-11; перед ним нужно закрыть отмеченные в матрице пробелы профильных гайдов.

## Приёмка этапов 0–1

| Критерий | Результат |
|---|---|
| Этап 0: ветка, SHA, dirty state, база и исходные метрики | PASS; данные выше, пользовательский план сохранён |
| TASK-01: master-индексы и контракт схемы | PASS; устаревшие v11/v13 убраны, RU/EN гайды и код показывают v16 |
| TASK-02: UI-название и внутренние `ModbusDemo`-имена | PASS; четыре вкладки сверены с `WorkspaceView.axaml` |
| TASK-03: RPM-инструкции | PASS; `apt-get install` получает явный абсолютный путь в root-shell; установка на ALT Linux — NOT RUN |
| TASK-04: смысл `user`/`admin` и граница PLC safety | PASS; текст не обещает аутентификацию или PLC interlock средствами UI |
| TASK-05: статус OPC UA | PASS; описаны компоненты, наличие тестового сценария, отсутствие вкладки и defaults None/Anonymous; промышленная проверка — NOT RUN |
| Документационные проверки | PASS: `git diff --check`, 19 локальных Markdown-ссылок без отсутствующих файлов, адресная сверка терминов/схемы/RPM; `dotnet msbuild AgentDocs/AgentDocs.csproj -getItem:None` подтверждает включение этого файла |
| Продуктовые тесты | NOT RUN: правились только Markdown-файлы |

Этап 2 можно начинать. Сокращение `AGENTS.md` (TASK-11) остаётся закрытым до переноса всех отмеченных инвариантов в действующие руководства.
