# PromFlow.Dispatcher — независимая приёмка оптимизации документации

## 1. Executive Summary

- Репозиторий: `zdanilv/PromFlow.Dispatcher`; рабочая ветка: `17-demo-v2`.
- База `16-demo-v1` и merge-base: `eaa8f53309dbf8d0fb73673654276f0fca759661`.
- Проверенный HEAD: `a2b15a778a708b05ac5a1e22404638f9fe085df9`.
- Область: TASK-01…TASK-20 из `docs/plans/PromFlow_Documentation_Optimization_Implementation_Plan_17-demo-v2.md`, коммитный diff и рабочее дерево, README, AGENTS, действующие гайды, checker и соответствующие участки кода. План до приёмки был неотслеживаемым (`?? docs/`), индекс и рабочее дерево не содержали других изменений.
- **Итог: BLOCKED.** Основные результаты оптимизации созданы, продуктовые файлы не менялись, критические запреты в AGENTS сохранены. Но действующий Route Map guide противоречит коду и Modbus guide в семантике команды `СБРОС` (D-01, HIGH). Такой контракт нельзя принять для промышленного приложения, даже если ошибка уже существовала в базе. Дополнительно есть два неблокирующих пробела (D-02, D-03).
- Токены/лимиты: **TOKEN SAVINGS NOT MEASURED**. Изменение размера файлов не служит измерением расхода Codex.

## 2. Матрица TASK-01…TASK-20

| TASK | Требование | Фактический результат | Подтверждение | Статус |
|---|---|---|---|---|
| 01 | Актуальная Route Map schema в RU/EN master | Оба индекса ссылаются на константу; примеры RU/EN дают v16 | `RouteMapConfigurationDocument.cs:38`; `00-master.ru.md:25`; `00-master.en.md:27`; оба `02` | PASS |
| 02 | Развести имя UI `Modbus TCP` и техническое `ModbusDemo` | Четыре вкладки и внутренний VM названы верно | `WorkspaceView.axaml:39-49`; оба `01-architecture`; оба master | PASS |
| 03 | Исправить потерю `RPM_PATH` через `su -` | README и guide 08 используют абсолютный путь внутри root-shell | `README.md:43-51`; `08-build-and-install.ru.md:433-445`; ALT smoke не выполнялся | PASS |
| 04 | `user/admin` не выдавать за промышленную авторизацию | UI-видимость и ответственность PLC разделены | `README.md:55-66`; `AGENTS.md:11-12`; `WorkspaceView.axaml:35-58` | PASS |
| 05 | Честный статус OPC UA | Код/DI и тесты описаны, вкладка Workspace не обещана | `README.md:22-26`; `10-opc-ua-guide.ru.md:5-10`; `Program.cs:133`; `WorkspaceView.axaml` | PASS |
| 06 | RU master как маршрут | Есть таблица «задача → гайд → код/тест», 01–11 и статус source | `00-master.ru.md:5-39`; checker 0 broken links | PASS |
| 07 | EN master и статус переводов | Маршруты на существующие EN 01–07 и RU 08–11, без v11 | `00-master.en.md:5-48`; checker | PASS |
| 08 | Пометить `source/ru` историческим | Оба индекса указывают границу и конкретное опасное старое утверждение | `00-master.ru.md:31-46`; `00-master.en.md:33-48` | PASS |
| 09 | Создать точный OPC UA guide | Слои, UI-граница, None/Anonymous, lifecycle, reconnect и тесты описаны; ключевые claims сверены с кодом | `10-opc-ua-guide.ru.md:7-98`; `DependencyInjection.cs:26-52`; client/runtime/server | PASS |
| 10 | Создать Code Map | Маршруты для Boot, Route Map, Modbus, OPC UA, конфигурации и тестов; цели Markdown-ссылок существуют | `11-code-map.ru.md:26-45`; checker; выборочные профильные тесты | PASS |
| 11 | Сократить AGENTS без потери high-risk правил | Сокращён; прямая safety-формулировка и профильные детали сохранены. D-01 — противоречие старого гайда, не потеря правила в AGENTS | `AGENTS.md:11-25,47-55`; таблица инвариантов ниже; diff с базой | PASS |
| 12 | Экономная стратегия агентов | Совпадает с 8 TOML ролями и лимитом 2; нет обязательного множества агентов или обещания экономии | `AGENTS.md:30-45`; `.codex/config.toml:3-10`; `.codex/agents/*.toml` | PASS |
| 13 | Краткий, точный README с сохранёнными медиа-задачами | Основной README точен и ссылки работают; шесть будущих снимков остались только в неотслеживаемом плане, не в доставляемом issue/checklist (D-03) | `README.md`; план `:506-513`; `git ls-files docs/plans` пуст | PARTIAL |
| 14 | Синхронизировать RU/EN архитектуру | Слои, UI и OPC UA отражены, но RU-перечень изменяемых секций пропускает `OpcUa`, EN и guide 09 включают (D-02) | `01-architecture-overview.ru.md:23-26`; EN `:19-23`; `Program.cs:103-133` | PARTIAL |
| 15 | Матрица тестов и диагностики | Матрица по риску, PASS/FAIL/NOT RUN, loopback, checker и ограничения платформ есть | `06-testing-and-diagnostics.ru.md:20-127` | PASS |
| 16 | `12-config-contracts` только при доказанной пользе | Не создан; `02`, `03`, `09`, `10` и Code Map покрывают маршрутизацию без второго контракта | План `:567-581`; перечень `AgentDocs/ru` | NOT REQUIRED |
| 17 | ADR/operator guide лишь при необходимости | Новых архитектурных решений или отдельного учебного сценария не заявлено; документы не созданы | План `:583-589`; перечень diff | NOT REQUIRED |
| 18 | Offline checker ссылок и изображений | Запуск 0 ошибок; 4 unit tests; отрицательные фикстуры возвращают 1, исправленная 0; якоря честно исключены | `tools/docs/check_markdown_links.py`; вывод проверок ниже | PASS |
| 19 | Согласованность schema/UI/Modbus/RPM/OPC UA/Code Map/master | Перечисленные адресные сверки выполнены; расширенная сверка команд обнаружила D-01, RU/EN — D-02 | код и гайды `02:105-107`, `03:95-98`; разделы 4–6 | FAIL |
| 20 | Необязательный docs CI | Workflow отсутствует; локальный checker и команда в guide 06 есть. Нет доказательства запуска GitHub Actions | `git ls-files .github/workflows` пуст; `06-testing-and-diagnostics.ru.md:32-52` | DEFERRED |

Итого: PASS 14, PARTIAL 2, FAIL 1, BLOCKED 0, NOT REQUIRED 2, DEFERRED 1.

## 3. Проверка README.md

- Назначение, .NET 10/Avalonia и целевые Windows 10/11, ALT Linux 11.1 согласуются с `global.json`, `Configurator.Desktop.csproj` и build guide 08. Quick Start указывает существующие `DesktopTemplate.slnx` и `Configurator.Boot.csproj`, использует PowerShell/Bash синтаксис; сами restore/run не выполнялись.
- Admin-вкладки точно соответствуют `WorkspaceView.axaml:39-49`; user mode скрывает вкладки, но не представляет видимость как авторизацию. `WorkspaceViewModel.cs:43-70` подтверждает общий runtime/monitor. Предупреждение о PLC заметно. `Modbus.DataMap`, `Modbus.AlarmMap`, demo DataMap и `system.fault` разделены.
- OPC UA назван как реализованные сервисы без Workspace entry point; None/Anonymous не названы промышленной защитой. Пути `%LOCALAPPDATA%\Configurator` и `${XDG_DATA_HOME:-$HOME/.local/share}/Configurator` совпадают с guide 09 и `ApplicationConfigPaths.cs`. Backup и запрет перезаписи user profile описаны.
- RPM пример больше не переносит `RPM_PATH` через `su -`; оба документа требуют доступный root абсолютный путь. Факт установки на ALT Linux не подтверждён. README не ссылается на PNG, 26 локальных ссылок существуют. Шесть скрытых исходных TODO удалены; их список есть лишь в неотслеживаемом плане (D-03).
- Метрики Git blob UTF-8: 19 723 → 10 703 байт (**−45,73%**); 220 → 137 строк (`str.splitlines()`); 17 → 12 заголовков; 31 → 26 локальных Markdown-ссылок по одинаковому простому шаблону. Рабочая копия README: 10 840 байт из-за CRLF; для процента сопоставлены blob с blob. Цель ~7–11 КБ достигнута по размеру, ориентир 90–130 строк превышен на 7, но он желательный.

## 4. Проверка AGENTS.md

Базовый AGENTS имел 19 615 байт, 140 строк и 15 заголовков; текущий blob — 9 823 байта (**−49,92%**), 55 строк и 7 заголовков. Сокращены таблица маршрутов, подробные PLC/Route Map/Modbus/OPC UA инструкции, список команд сборки и шаблон делегирования. Удалены привязка к `16-demo-v1` и фиксированная цифра schema. Постоянный safety gate, Git hygiene, пропорциональная проверка и контракт отчёта остались непосредственно в AGENTS. Подробности перенесены в `02`, `03`, `04`, `06`, `09`, `10` и Code Map. Ниже проверено содержание, а не только наличие ссылок.

| Инвариант | Был в базовом AGENTS | Где сейчас | Подтверждение | Итог |
|---|---|---|---|---|
| PLC — окончательная управляющая/safety authority; UI не interlock | §1, §4 | `AGENTS.md:11`; `01-architecture.ru.md:187-192` | Прямая формулировка | PASS |
| Live writes/start/stop/reset/ACK/field mapping лишь с разрешением и безопасной средой | §1, §4 | `AGENTS.md:12`; `10-opc-ua-guide.ru.md:68` | Прямой запрет | PASS |
| `user/admin` — интерфейс, не аппаратная авторизация | §4 | `AGENTS.md:11,25`; `README.md:55-66` | XAML `:35-58` | PASS |
| `Modbus.DataMap` / `AlarmMap` / `ModbusDemo.DataMap` раздельны | §4 | `AGENTS.md:23`; `03-modbus.ru.md:15-26` | DI/Workspace и guide | PASS |
| `system.fault` — PLC Read/Bool в DataMap | §4 | `AGENTS.md:23`; `03-modbus.ru.md:191-200` | Прямая формулировка | PASS |
| Register-bit RMW сохраняет соседние биты | §4 | `AGENTS.md:15`; `03-modbus.ru.md:235-240` | `ModbusTcpServiceTests.cs:207-242`; `ModbusBitWriterTests.cs:31-53` | PASS |
| Порядок команд и readback/feedback | §4 | `AGENTS.md:14-15,24`; `02-route-map.ru.md:105-128` | Start/Stop и selector указаны; D-01 — исключение для reset | PASS с D-01 |
| Нет автоповтора неидемпотентной команды | §4 | `AGENTS.md:14`; `03-modbus.ru.md:231-232` | Прямой запрет | PASS |
| `system.reset` — Pulse, никогда Latched | §4 | `AGENTS.md:24`; `03-modbus.ru.md:95-98` | `TopBarViewModel.cs:284-303`; противоречит `02:105-107` (D-01) | FAIL в системе гайдов |
| Alarm ACK — ограниченный pulse, repeat/clear по состоянию | §4 | `AGENTS.md:15`; `03-modbus.ru.md:114-175` | `06-testing.ru.md:101-102,162-185` | PASS |
| Good/Bad/Stale и snapshot без повторной команды | §4 | `AGENTS.md:14`; `02-route-map.ru.md:82-99,116-124` | Качество и readback | PASS |
| UI updates на Avalonia thread | §4 | `AGENTS.md:14`; `05-coding-rules.ru.md`; `10-opc-ua-guide.ru.md:49` | Явное требование | PASS |
| Route Map использует доменные SignalId, адреса в DataMap | §4 | `AGENTS.md:23`; `04-signal-id.ru.md:1-4`; `03-modbus.ru.md:3-26` | Разделение сохранено | PASS |
| Mutable definition принадлежит manager, не singleton definition | §4 | `AGENTS.md:22`; `01-architecture.ru.md:131-143`; `02-route-map.ru.md:201-207` | Boot `Program.cs:136-139` | PASS |
| Versioned JSON, миграции, round-trip, unknown data | §4 | `AGENTS.md:13,22`; `02-route-map.ru.md:39-55,201-210` | `CurrentSchemaVersion=16`, `RouteMapConfigurationTests.cs` | PASS |
| Start/Stop взаимно исключены; OffFeedback read-only | §4 | `AGENTS.md:24`; `02-route-map.ru.md:109-128` | Порядок и отсутствие обратной записи | PASS |
| Selector C: две команды, порядок, Enabled=false однократно | §4 | `AGENTS.md:24`; `02-route-map.ru.md:89-97,115-124` | ReadWrite/Bool, readback и reset | PASS |
| Offline setpoint и максимум одна auto-send попытка | §4 | `AGENTS.md:24`; `02-route-map.ru.md:133-146` | `06-testing.ru.md:211` | PASS |
| OPC UA Application/SDK boundary | §4 | `AGENTS.md:16`; `10-opc-ua-guide.ru.md:12-22` | `CleanArchitectureTests.cs` | PASS |
| OPC UA identity/certificates/trust; не downgrade | §4 | `AGENTS.md:16`; `10-opc-ua-guide.ru.md:62-64` | DI `DependencyInjection.cs:34-35` | PASS |
| OPC UA lifecycle/cancellation/subscriptions/concurrency/disposal | §4 | `AGENTS.md:16`; `10-opc-ua-guide.ru.md:51-60` | Ограничение reconnect/dispose указано | PASS |
| OPC UA write access/typed tags/реальные пределы UI | §4 | `AGENTS.md:16`; `10-opc-ua-guide.ru.md:38-49,62-68` | `WorkspaceView.axaml` без OPC UA tab | PASS |
| Профиль/portable paths; installer не стирает настройки | §4 | `AGENTS.md:17,25`; `09-configuration.ru.md:5-16,57-70` | `ApplicationConfigPaths.cs:13-29` | PASS |
| Минимальный scope, сохранение правок, безопасный Git | §1, §6 | `AGENTS.md:13,21,53` | Прямые ограничения | PASS |
| Именованные тесты, full suite по риску, честный отчёт | §1, §5–6 | `AGENTS.md:47-55`; `06-testing.ru.md:90-127` | PASS/FAIL/NOT RUN | PASS |
| VS visibility, IDs, designer metadata, Linux resources | §4 | `AGENTS.md:26`; `05-coding-rules.ru.md`; `08-build.ru.md` | `dotnet msbuild AgentDocs.csproj -getItem:None` увидел новые guides | PASS |

Стратегия: `.codex/config.toml` по Git не менялся; `gpt-6-sol/high`, default Luna/medium, лимит 2. Все 8 ролей из таблицы AGENTS имеют соответствующие TOML модели/effort. Reviewer TOML прямо требует read-only. Текущий аудит включал один узкий независимый review safety-инвариантов; не утверждается, что была измерена экономия от такой маршрутизации или что фактически запущенная модель совпадала с проектной конфигурацией.

## 5. Проверка технической документации

- **Master RU/EN:** ссылки проходят; версия schema берётся из кода, EN честно указывает покрытие 01–07 и RU 08–11. Оба объясняют исторический `source/ru` и прямо предупреждают об устаревшем autostart. Ни существующие source-файлы, ни миграции не переписаны.
- **Architecture RU/EN:** слои, зависимости, четыре вкладки, общая Modbus lifecycle и отсутствие OPC UA entry point соответствуют `Program.cs`, `WorkspaceView.axaml`, `WorkspaceViewModel.cs`. В RU пропущен `OpcUa` в перечне mutable overlay, хотя EN и guide 09 называют его (D-02). `Application.WorkMode` берётся из launch configuration; runtime overlay включает user config (`Program.cs:103-125`).
- **Route Map / Modbus / SignalId:** текущий JSON v16 подтверждён кодом; DataMap/AlarmMap/demo DataMap и `system.fault` разделены. Readback, quality, selector, Start/Stop, OffFeedback, offline setpoint, RMW, ACK изложены. Исключение: ошибка `СБРОС` в `02:105-107` противоречит `03:95-98` и коду (D-01).
- **OPC UA:** guide 10 соответствует Application contracts, Infrastructure DI/client/server/runtime, Boot, Desktop и тестам; разделяет код и доступность UI. None/Anonymous, отключённые сертификаты, частичный reconnect и ограничение индивидуального Dispose описаны без production-гарантий. Внешний endpoint не тестировался.
- **Code Map:** все Markdown-linked цели существуют; выборочно сверены `RouteMapConfigurationTests`, `ModbusTcpServiceTests`, `ModbusBitWriterTests`, `OpcUaRuntimeTests`, `OpcUaIntegrationTests`, `AppConfigServiceTests` с заявленными областями. Кодовая карта не объявляет headless тест platform smoke или регистрацию DI запуском OPC UA.
- **Testing/Diagnostics:** guide 06 содержит выбор тестов по типу изменения, loopback, статусы отчёта, ручные consistency steps и честные пределы checker. **Build/Install:** RPM пример синхронизирован с README; установки ALT Linux не было. **Configuration:** guide 09 различает поставляемые defaults, user profile, import/export и резервное копирование.
- **Исторические/условные:** `source/ru` помечен справочным; `12-config-contracts`, ADR, operator guide не созданы обоснованно. Нового конкурирующего полного контракта не обнаружено.

## 6. Проверка автоматических инструментов

| Инструмент/проверка | Выполнено | Результат | Доказательство |
|---|---|---|---|
| `python tools/docs/check_markdown_links.py` | Да | PASS: 32 MD, 352 локальные ссылки, 0 изображений в реальных MD, 26 external ignored, 0 broken, 0 fragments | Реальный stdout/exit 0 |
| `python -m unittest tools.docs.test_check_markdown_links -v` | Да | PASS: 4/4, включая missing file/image, Unicode/URL-encoding, reference links, error exit 2 | Реальный stdout/exit 0 |
| Дополнительная временная fixture: ошибочный относительный путь, missing image, корректный Unicode target, внешний URL | Да | PASS как негативная проверка: exit 1, `Broken local links: 2`; после исправления exit 0 | Запуск checker с `--root` на временном каталоге под `tools/docs`; каталог удалён |
| Якоря, multiline/HTML/shortcut ссылки, внешние URL | Нет | NOT VERIFIED инструментом; он сообщает ограничение. В проверенных файлах checker показал 0 fragment links | `check_markdown_links.py:113-174`; guide 06 `:48-52` |
| Ручная сверка schema/UI/RPM/OPC UA/Code Map и команд | Да | FAIL по D-01, частично по D-02; остальные адресные факты подтверждены | Раздел 5 и дефекты |
| `dotnet msbuild AgentDocs/AgentDocs.csproj -getItem:None` с фильтром новых MD | Да | PASS: 10, 11 и stage report включены glob | Вывод MSBuild |
| `git diff --check 16-demo-v1...HEAD`; `git diff --check` | Да | PASS, пустой вывод, exit 0 | Git |
| .NET build/test, запуск приложения, RPM/Windows installer, ALT Linux smoke, GitHub Actions | Нет | NOT RUN: docs-only diff, не требуется для проверки Markdown; OS/CI результат не заявлен | Отсутствуют соответствующие запуски/workflow |

Первый запуск дополнительной fixture в системном `%TEMP%` не смог создать каталог из-за sandbox permissions; повтор в разрешённом временном каталоге под `tools/docs` завершился указанными результатами. На Windows для Unicode stdout fixture задан `PYTHONIOENCODING=utf-8`; это настройка тестового subprocess, checker не менялся. Checker охватывает `docs/plans`, но не `docs/reports`: данный отчёт после создания не входит в его 32 файла; ссылки в отчёте даны как code paths, а не как Markdown-link targets.

## 7. Проверка Git diff

Сравнение `16-demo-v1...HEAD`: 14 файлов, 912 вставок и 437 удалений. Категории: `AGENTS.md` (1), `README.md` (1), `AgentDocs` (10: RU master/architecture/Modbus/testing/build, EN master/architecture, новые OPC UA, Code Map, stage report), `tools/docs` (2 checker/test). Полный перечень: `AGENTS.md`; `README.md`; `AgentDocs/en/00-master.en.md`, `01-architecture-overview.en.md`; `AgentDocs/ru/00-master.ru.md`, `01-architecture-overview.ru.md`, `03-modbus-tcp-guide.ru.md`, `06-testing-and-diagnostics.ru.md`, `08-build-and-install.ru.md`, `10-opc-ua-guide.ru.md`, `11-code-map.ru.md`, `documentation-check-17-demo-v2-stage-0-1.ru.md`; `tools/docs/check_markdown_links.py`, `test_check_markdown_links.py`.

`git diff --name-status` и `git diff --cached --name-status` до отчёта пусты. Единственное исходное неотслеживаемое содержимое — основной план под `docs/plans/`; он не входит в HEAD. Базовая ссылка по текущему Git всё ещё `eaa8f533...`, равна исходной SHA в плане/stage report и merge-base; нельзя доказать всю историю удалённого ref без внешнего журнала, но свидетельств его изменения в этом checkout нет. C#, AXAML, product JSON, csproj, packaging, `.codex`, установщики и бинарные файлы в diff отсутствуют. Продуктовая функциональность этим diff не менялась.

## 8. Метрики до и после

Метод: `git show 16-demo-v1:<path>` и `git show HEAD:<path>`; UTF-8 длина Git blob; строки `str.splitlines()`; заголовки `^#{1,6} `; локальные ссылки — одинаковый простой шаблон inline Markdown (не оценка всех синтаксических форм). CRLF в рабочем дереве может увеличить фактическое число байтов.

| Метрика | 16-demo-v1 | 17-demo-v2 HEAD | Изменение |
|---|---:|---:|---:|
| README UTF-8 байт | 19 723 | 10 703 | −9 020 / −45,73% |
| README строк | 220 | 137 | −83 |
| README заголовков | 17 | 12 | −5 |
| README локальных inline-ссылок | 31 | 26 | −5 |
| AGENTS UTF-8 байт | 19 615 | 9 823 | −9 792 / −49,92% |
| AGENTS строк | 140 | 55 | −85 |
| AGENTS заголовков | 15 | 7 | −8 |
| RU master UTF-8 байт / строки | 7 599 / 72 | 10 138 / 46 | +2 539 / −26 строк |
| EN master UTF-8 байт / строки | 3 253 / 45 | 7 840 / 48 | +4 587 / +3 строки |
| Отдельный Code Map | нет | `11-code-map.ru.md` | есть |
| Постоянные safety-ограничения в AGENTS | есть | есть, короче | сохранены по таблице |
| Дублирование больших технических глав в README/AGENTS | большое (таблицы и подробности) | сокращено, перенесено в профильные гайды | качественная оценка по diff; автоматического числа нет |
| Фактическое usage Codex | нет A/B данных | нет A/B данных | **TOKEN SAVINGS NOT MEASURED** |

Рост master в байтах объясняется ссылочной таблицей и явными RU/EN/historical статусами; оба остаются короткими по строкам, но утверждать уменьшение всего объёма документации или токенов нельзя. Для A/B нужны отдельные чистые сессии, одинаковые задачи BENCH-01…07 из плана, одинаковый код/модель/effort, фактические `input_tokens`, cached/output/reasoning, повторы и принятые результаты. Этот контекст для такого опыта уже «разогрет» чтением документов.

## 9. Найденные дефекты

| ID | Severity | Файл | Проблема, подтверждение | Исправление | Блокирует приёмку |
|---|---|---|---|---|---|
| D-01 | HIGH | `AgentDocs/ru/02-route-map-guide.ru.md:105-107` | Требуется Pulse reset. Текст относит `СБРОС` к toggle и предписывает UI отправку `false` при снятии. Код `TopBarViewModel.cs:284-303` отправляет только `true`; `03-modbus-tcp-guide.ru.md:95-98` описывает Pulse true→false на уровне dispatcher. Ошибка уже есть в `16-demo-v1`, но сохраняет неверный действующий контракт для PLC-команды. | Исключить `СБРОС` из общего toggle-правила и отдельно описать momentary/Pulse, единственный UI dispatch `true`, readback и сброс dispatcher; сверить RU/EN. | **Да** |
| D-02 | MEDIUM | `AgentDocs/ru/01-architecture-overview.ru.md:23-26` | RU перечень изменяемых runtime-секций пропускает `OpcUa`; EN `01:19-23`, guide `09:25-28` и `Program.cs:103-133` подтверждают user overlay и binding `OpcUa`. Вводит в заблуждение при поиске конфигурации. | Добавить `OpcUa` в RU перечень и сверить описание оверлея с EN/09. | Нет самостоятельно |
| D-03 | MEDIUM | `README.md` и неотслеживаемый `docs/plans/...md:506-513` | План требует сохранить шесть медиа-задач в отдельном issue/checklist. В README 6 исходных скрытых TODO удалены; список теперь есть только в неотслеживаемом плане (`git ls-files docs/plans` пуст), значит чистый checkout HEAD теряет этот backlog. | После согласованного оформления работ сохранить checklist/issue в долговечном отслеживаемом месте; не добавлять несуществующие PNG. | Нет самостоятельно |

## 10. Невыполненные и отложенные задачи

- TASK-13 PARTIAL: медиа-backlog не доставляется с HEAD (D-03); функциональная и safety часть README принята.
- TASK-14 PARTIAL: RU/EN перечисление runtime конфигурации расходится (D-02).
- TASK-19 FAIL: команда reset в действующем guide описана неверно (D-01), что не позволяет закрыть consistency gate.
- TASK-16 и TASK-17 NOT REQUIRED: нет доказанной пользы от нового config-contracts guide, ADR или operator guide в рамках документационной оптимизации.
- TASK-20 DEFERRED: локальная проверка описана и работает; GitHub Actions workflow отсутствует. Если проекту нужен обязательный PR gate, это отдельное решение, не доказанный текущий PASS CI.

## 11. Остаточные риски

- D-01 может направить будущую правку UI/Modbus reset по неверной модели; до исправления руководства приоритет имеют код, тесты и явное правило `AGENTS.md:24` о Pulse. PLC остаётся конечной safety authority; ни один live endpoint не использовался.
- Пользовательские конфигурации и обновления проверены по тексту и коду путей, но не по реальной установке/миграции на Windows/ALT Linux. RPM shell-синтаксис проверен логически, не OS smoke.
- Checker проверяет существование простых локальных целей, не якоря, HTML/multiline/shortcut ссылки, семантику C# и не `docs/reports`. Нулевые ошибки не равны полной технической корректности.
- Рабочий план не отслеживается Git; чистый checkout HEAD не содержит его и медиа-backlog. Продуктовые build/test не запускались, так как diff документационный; состояние существующего приложения от этого аудита не менялось.
- Не измерены реальное использование токенов, время, лимиты или экономия Codex; сокращение AGENTS на 49,92% — только размер файлового контекста, не usage.

## 12. Финальное решение

**BLOCKED** для окончательной приёмки `17-demo-v2`. Критические правила в AGENTS сохранены, ссылки/checker проходят, diff ограничен документацией, однако HIGH-дефект D-01 нарушает обязательную техническую достоверность действующего руководства о PLC-команде. Для повторной полной приёмки нужно устранить D-01, согласовать D-02, долговечно сохранить медиа-backlog D-03, повторить checker и адресную сверку reset/конфигурации на той же ветке. Опциональный CI может остаться отложенным; A/B usage следует оценивать отдельно, если нужны доказательства экономии. Этот аудит не изменял обнаруженные дефекты и не выполнял продуктовых команд.
