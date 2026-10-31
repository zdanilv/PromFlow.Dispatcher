# PromFlow.Dispatcher — повторная приёмка исправлений D-01…D-03

## 1. Состояние проверки

- Рабочая ветка: `17-demo-v2`; проверенный HEAD: `a2b15a778a708b05ac5a1e22404638f9fe085df9`.
- База сравнения: `16-demo-v1` (`eaa8f53309dbf8d0fb73673654276f0fca759661`).
- Исходный `docs/reports/DocumentationOptimizationAcceptanceReport_17-demo-v2.md` оставлен без изменений как снимок решения **BLOCKED**. Исходный неотслеживаемый план также не менялся.
- До правок `git diff --name-status` и `git diff --cached --name-status` были пусты, `git status --short` показывал только `?? docs/`. После правок изменены два действующих RU guide; создан отдельный медиа-чек-лист и этот отчёт. Продуктовые `.cs`, `.axaml`, `.json`, `.csproj`, `.codex`, `README.md` и `AGENTS.md` не менялись.

**Вывод:** D-01…D-03 устранены в рабочем дереве. Чистый checkout текущего HEAD всё ещё содержит прежний текст D-01/D-02 и не содержит медиа-чек-лист: коммит сознательно не выполнялся. Поэтому это **PASS проверки исправленного рабочего дерева**, но **не PASS приёмки HEAD**; исходный вердикт BLOCKED для HEAD остаётся актуальным до фиксации изменений и новой проверки зафиксированного состояния.

## 2. Повторная проверка дефектов

| ID | Прежнее состояние | Результат в рабочем дереве | Доказательство и граница результата |
|---|---|---|---|
| D-01 / HIGH | `02-route-map-guide.ru.md:105-107` относил `СБРОС` к toggle/readback и обещал UI-запись `false` при снятии | `СБРОС` исключён из toggle-перечня; отдельный абзац описывает `system.reset` / `ResetCommand`, `ReadWrite/Bool/Pulse`, UI dispatch только `true`, завершение импульса dispatcher после `PulseDurationMs`, моментальную индикацию и readback | `AgentDocs/ru/02-route-map-guide.ru.md:106-114`; `TopBarViewModel.cs:284-303` отправляет `true`; `RouteMapSignalMappingViewModel.cs:475-478` требует Pulse; `03-modbus-tcp-guide.ru.md:95-98` совпадает. EN `02-route-map-guide.en.md:81-84` уже исключал Reset из toggle-списка и не менялся. |
| D-02 / MEDIUM | RU architecture не называл `OpcUa` среди изменяемых секций user overlay | `OpcUa` включён в перечень и описание общей конфигурации; install-level `Application.WorkMode` оставлен отдельно. Оговорка об отсутствии автозапуска только от DI и отсутствии Workspace-вкладки сохранена | `AgentDocs/ru/01-architecture-overview.ru.md:19-26,166-174`; EN architecture `:19-23`; guide 09 `:23-28`; `Program.cs:103-133` использует shared runtime configuration для OPC UA, но launch configuration для `ApplicationOptions`. |
| D-03 / MEDIUM | Шесть будущих снимков перечислялись только в неотслеживаемом основном плане | Создан отдельный `docs/plans/DocumentationMediaChecklist_17-demo-v2.md`: шесть точных имён, состояния UI и критерии безопасной подготовки. PNG и ссылки на отсутствующие изображения не добавлены | Чек-лист `:5-12`; основной план `:506-515`; автоматическая проверка подтвердила 6 пунктов, каждое имя встречается один раз. Файл пока не входит в HEAD без будущего коммита. |

Независимый read-only reviewer сверил изменённые guide и чек-лист с кодом и планом; замечаний не обнаружил. Reviewer не выполнял команд или подключений к устройствам.

## 3. Затронутые TASK

| TASK | Первая приёмка | Повторная оценка рабочего дерева | Статус HEAD до коммита |
|---|---|---|---|
| 13 — README и медиа-backlog | PARTIAL из-за D-03 | PASS: отдельный чек-лист создан, README не содержит сломанных ссылок на PNG | PARTIAL: чек-лист отсутствует в HEAD |
| 14 — RU/EN architecture | PARTIAL из-за D-02 | PASS: описание `OpcUa` overlay согласовано с EN и кодом | PARTIAL: RU guide в HEAD ещё прежний |
| 19 — consistency | FAIL из-за D-01 и D-02 | PASS по затронутым контрактам reset и OPC UA; адресная сверка schema/UI/RPM из первой приёмки остаётся без новых изменений | FAIL: исходное противоречие reset остаётся в HEAD |

Остальные TASK из исходной матрицы не переоценивались без нового повода; новые правки не затрагивают их файлы или контракты. Опциональный CI остаётся DEFERRED, фактическая экономия токенов — **TOKEN SAVINGS NOT MEASURED**.

## 4. Выполненные проверки

| Команда / проверка | Результат |
|---|---|
| `git branch --show-current`; `git rev-parse HEAD`; `git status --short`; `git diff --name-status`; `git diff --cached --name-status` до и после | PASS: ветка `17-demo-v2`, SHA выше; кроме двух изменённых guide и неотслеживаемого `docs/` иных изменений нет; индекс пуст |
| `git diff --check` | PASS: exit 0, замечаний по пробелам нет |
| `python tools/docs/check_markdown_links.py` | PASS: 33 MD, 353 локальные ссылки, 0 отсутствующих целей, 0 изображений в реальной документации; это на один файл и одну ссылку больше, чем при первой приёмке, поэтому новый чек-лист охвачен |
| `python -B -m unittest discover -s tools/docs -p 'test_*.py' -v` | PASS: 4/4 теста checker |
| `dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMapSignalRuntimeAndMappingTests"` | PASS: 43/43; компиляция выдала предупреждения в не изменённом коде Desktop, ошибок нет |
| `dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~RouteMapModbusAdapterTests"` | PASS: 5/5; только локальные тестовые сценарии |
| Проверка нового чек-листа | PASS: 6 пунктов, все шесть имён из плана по одному разу, нет trailing whitespace и ссылок на отсутствующие PNG |
| Сверка reset/OPC UA с кодом, RU/EN guides и независимый read-only review | PASS по D-01/D-02/D-03; новых фактических противоречий не найдено |
| Полный solution build/test, запуск приложения, installer/RPM, ALT Linux smoke, промышленные endpoint | NOT RUN: документационные правки не требуют этих сценариев; разрешения на live operations нет |

Checker проверяет `docs/plans`, но не `docs/reports`; этот новый отчёт не входит в число 33 проверенных MD. В отчёте нет относительных Markdown-ссылок, а пути и имена указаны моноширинным текстом; существование названных исходных файлов проверено адресно. Якоря checker не проверяет и сообщает об этом явно.

## 5. Остаточные ограничения и решение

- Нет изменений runtime, PLC mappings, конфигурации пользователей или функциональности UI. Производственные PLC/OPC UA endpoints и installer не использовались.
- Основной план, исходный отчёт, новый чек-лист и этот отчёт находятся в `docs/` как неотслеживаемые файлы. Их наличие в рабочем дереве не означает наличие в коммите; при чистом checkout HEAD D-01…D-03 остаются открытыми.
- **Решение для рабочего дерева: PASS по исправлениям D-01…D-03. Решение для HEAD: BLOCKED до отдельного коммита и проверки зафиксированного diff.** Коммит, push и merge в этой работе не выполнялись по выбранному ограничению.
