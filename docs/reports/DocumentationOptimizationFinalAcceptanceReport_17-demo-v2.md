# PromFlow.Dispatcher — финальная приёмка оптимизации документации

## 1. Решение

**PASS WITH DEFERRED OPTIONAL ITEMS** для состава документов, включённого в локальный коммит закрытия приёмки на ветке `17-demo-v2`. Базовая ветка — `16-demo-v1` (`eaa8f53309dbf8d0fb73673654276f0fca759661`); HEAD до закрытия — `a2b15a778a708b05ac5a1e22404638f9fe085df9`. Идентификатор итогового коммита следует получать через `git rev-parse HEAD`: собственный SHA нельзя записать в файл того же коммита.

Исходный независимый отчёт `DocumentationOptimizationAcceptanceReport_17-demo-v2.md` фиксирует BLOCKED до исправлений. Отчёт `DocumentationOptimizationReacceptanceReport_17-demo-v2.md` фиксирует PASS исправленного рабочего дерева и BLOCKED прежнего HEAD до коммита. Это исторические снимки; их оценки не являются оценкой итогового коммита. Критические правила PLC safety в AGENTS сохранены, D-01…D-03 закрыты, незапланированных продуктовых изменений нет.

## 2. Закрытие замечаний

| ID | Было | Стало и подтверждение | Итог |
|---|---|---|---|
| D-01 HIGH | RU Route Map guide относил `СБРОС` к toggle-командам с UI-записью `false` при снятии | `AgentDocs/ru/02-route-map-guide.ru.md:106-114` выделяет `system.reset` / `ResetCommand` как `ReadWrite/Bool/Pulse`: UI отправляет только `true`, завершает импульс Modbus dispatcher. Сверено с `TopBarViewModel.cs:284-303`, `RouteMapSignalMappingViewModel.cs:475-478`, `03-modbus-tcp-guide.ru.md:95-98` и EN Route Map guide. | ЗАКРЫТО |
| D-02 MEDIUM | RU architecture пропускал `OpcUa` в перечне пользовательских runtime-секций | `AgentDocs/ru/01-architecture-overview.ru.md:19-26` теперь включает `OpcUa`; install-level `Application.WorkMode` и отсутствие OPC UA-вкладки/автозапуска только от DI сохранены. Сверено с EN guide, guide 09 и `Program.cs:103-133`. | ЗАКРЫТО |
| D-03 MEDIUM | Шесть медиа-задач оставались лишь в неотслеживаемом плане | Отдельный `docs/plans/DocumentationMediaChecklist_17-demo-v2.md` содержит все шесть исходных имён и состояния экранов, требования к скрытию данных и запрет ссылок на ещё не созданные PNG. Чек-лист включён в коммит вместе с основным планом. | ЗАКРЫТО |

Первый отчёт сохранён без редактирования. В исходном плане лишь семь завершающих Markdown-разрывов заменены на `<br>` с тем же отображением, чтобы новый файл проходил `git diff --cached --check`; требования и задачи плана не менялись. После коммита план и отчёты становятся доступны в чистом checkout вместе с исправлениями и чек-листом. Ни реальные скриншоты, ни ссылки на отсутствующие изображения не создавались.

## 3. Матрица TASK-01…TASK-20

| TASK | Статус | Основание |
|---|---|---|
| 01 | PASS | RU/EN master берут версию Route Map schema из `CurrentSchemaVersion`; действующие примеры v16. |
| 02 | PASS | UI `Modbus TCP` отделён от внутренних `ModbusDemo` имён. |
| 03 | PASS | README и guide 08 больше не переносят `RPM_PATH` через `su -`. |
| 04 | PASS | `user/admin` описаны как режимы интерфейса, PLC — safety authority. |
| 05 | PASS | OPC UA infrastructure описана без обещания отсутствующей Workspace-вкладки. |
| 06 | PASS | RU master маршрутизирует к актуальному гайду, коду и тесту. |
| 07 | PASS | EN master указывает фактическое покрытие и RU-руководства для 08–11. |
| 08 | PASS | `AgentDocs/source/ru` обозначен историческим справочным материалом. |
| 09 | PASS | OPC UA guide описывает реализованные границы и ограничения security/lifecycle. |
| 10 | PASS | Code Map создан, целевые локальные пути проходят checker. |
| 11 | PASS | AGENTS сокращён без потери критических safety-инвариантов. |
| 12 | PASS | Маршрутизация агентов согласована с `.codex` и не обещает измеренную экономию. |
| 13 | PASS | README проверен; шесть медиа-задач сохранены в отдельном чек-листе. |
| 14 | PASS | RU/EN architecture согласованы по `OpcUa` user overlay (D-02). |
| 15 | PASS | Guide 06 содержит матрицу тестов, диагностику и ограничения checker. |
| 16 | NOT REQUIRED | Отдельный `12-config-contracts` создал бы второй источник уже описанных контрактов. |
| 17 | NOT REQUIRED | ADR и operator guide условны; новой архитектурной или учебной задачи не было. |
| 18 | PASS | Offline checker и его негативные тесты проходят. |
| 19 | PASS | Ошибка reset (D-01) и расхождение OPC UA (D-02) исправлены; остальные проверки из первой приёмки остаются действительными. |
| 20 | DEFERRED | Необязательный GitHub Actions workflow не добавлен; локальный checker документирован и воспроизводим. |

Итого: **PASS 17, PARTIAL 0, FAIL 0, BLOCKED 0, NOT REQUIRED 2, DEFERRED 1**. Незакрытых CRITICAL/HIGH замечаний нет.

## 4. Проверки и границы вывода

| Проверка | Фактический результат |
|---|---|
| `python tools/docs/check_markdown_links.py` | PASS: 33 Markdown-файла, 353 локальные ссылки, 0 broken links; новый чек-лист входит в охват. Якоря, HTML и некоторые сложные формы ссылок инструмент не проверяет. |
| `python -B -m unittest discover -s tools/docs -p 'test_*.py' -v` | PASS: 4/4. |
| `dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMapSignalRuntimeAndMappingTests"` | PASS: 43/43; предупреждения компилятора в не изменённом Desktop-коде. |
| `dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~RouteMapModbusAdapterTests"` | PASS: 5/5; локальный тестовый контур. |
| `git diff --check` и независимый read-only review D-01…D-03 | PASS: пробельных дефектов и новых фактических замечаний нет. |
| Проверка новых Markdown в `docs/reports` | PASS: отчёты не содержат относительных Markdown-ссылок; их отсутствие в охвате checker учтено отдельно. |
| Полная сборка/тесты решения, Windows/ALT installation smoke, live PLC/OPC UA endpoints, GitHub Actions | NOT RUN: продуктовый код не менялся; для платформенного и промышленного сценария нет соответствующей среды/разрешения; CI отсутствует. |

Diff закрытия приёмки включает только два RU guide и документы под `docs/plans`, `docs/reports`; README, AGENTS, `.codex`, C#, AXAML, JSON, csproj и packaging не менялись. Коммит локальный; push и merge не выполнялись. Фактическая экономия токенов Codex **TOKEN SAVINGS NOT MEASURED**: уменьшение Markdown-файлов не эквивалентно измерению usage.

## 5. Остаточные ограничения

Опциональный CI остаётся отложенным. Реальные скриншоты будут отдельной задачей по чек-листу. Поведение на промышленном оборудовании и ALT Linux installer не проверялось в документационной работе; PLC остаётся конечной инстанцией safety/interlock. Для этой приёмки достаточно проверенного docs-only diff, локальных ссылок, точной сверки команды reset и конфигурации OPC UA, профильных тестов и сохранения всех материалов в локальном коммите.
