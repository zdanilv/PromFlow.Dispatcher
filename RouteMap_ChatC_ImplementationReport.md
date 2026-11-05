# Route Map — отчёт чата C (этапы 4–5)

## 1. Общие сведения

- Репозиторий: `zdanilv/PromFlow.Dispatcher`; ветка: `17-demo-v2`.
- Исходный и итоговый HEAD: `8746425d70c3c2efb2be06db7e5411426e8b0cfa` (коммит не создавался).
- Оркестратор: Codex GPT-6; проектная конфигурация `.codex/config.toml` задаёт `gpt-6-sol/high`. Фактически задействованы роли `explorer`, `protocol_guard`, `avalonia_ui`, `implementer`, `tester`.
- Рабочее дерево: изменено, изменения этой задачи не закоммичены; исходно было чистым.
- Проверка предыдущих этапов 2–3: read-only проверка кода не обнаружила блокера; профильные и полные тесты этапа 5 пройдены.

## 2. Реализованные требования

| Требование | Статус | Изменённые файлы | Проверка |
|---|---|---|---|
| 4.1 Скрыть АВТОМАТ/РУЧНОЙ без пустого места | PASS | `TopBarView.axaml`, `.axaml.cs` | Headless TopBar, полный UI-проект |
| 4.2 Исключить команды режимов | PASS | `TopBarViewModel.cs`, `TopBarView.axaml` | Unit: инертные публичные команды, ноль запросов записи |
| 4.3 Убрать режимы из настроек, сохранить legacy JSON | PASS | `RouteMapSettingsDialog.axaml`, `RouteMapSettingsViewModel.cs` | Unit round-trip, headless настройки |
| 4.4 Исключить mode SignalId из активного inventory | PASS | `RouteMapSignalInventory.cs`, `RouteMapSignalMappingViewModel.cs` | Unit: нет TopBar mode/Enabled; custom reuse обнаруживается как конфликт |
| 4.5 Очистить defaults и сохранённый DataMap | PASS | `Configurator.Boot/appsettings.json`, mapping VM, `AppConfigService.cs`, `ModbusTcpProfileTransferService.cs` | Unit: exact IDs, backup, повторный save, import/export, остановленная роль |
| 4.6 Совместимость схемы 17 и старых JSON | PASS | legacy DTO сохранены; тесты конфигурации обновлены | `RouteMapConfigurationTests`, полный unit-проект |
| 5 Комплексные проверки карточки/миграций/режимов | PASS | пять тестовых файлов | Unit, Modbus, headless UI и полный solution test |
| 5 Документация RU/EN, README, AGENTS | PASS | текущие guides 01–06 RU/EN, README, AGENTS | Markdown links, поиск устаревших утверждений |

## 3. Список изменений

- `Configurator.Desktop/Workspace/RouteMap/Panels/TopBarView.axaml` и `.axaml.cs`: скрыты кнопки режимов, удалены их командные/указательные связи и зарезервированная колонка; СБРОС/АВАРИЯ/НАСТРОЙКИ/ПОМОЩЬ сохранены.
- `Configurator.Desktop/Workspace/RouteMap/ViewModels/TopBarViewModel.cs`: публичные команды режимов стали инертными для совместимости; активные записи удалены.
- `Configurator.Desktop/Workspace/RouteMap/Settings/RouteMapSettingsDialog.axaml` и `RouteMapSettingsViewModel.cs`: скрыты legacy-редакторы, запрещено добавление mode bindings, исторические данные сохраняются.
- `Configurator.Desktop/Workspace/RouteMap/SignalMapping/RouteMapSignalInventory.cs` и `RouteMapSignalMappingViewModel.cs`: legacy TopBar вне inventory; Save удаляет только exact mode IDs после проверки других usages и остановки client/server. Во время очистки runtime-карта не заменяется; следующий запуск роли читает сохранённые настройки.
- `Configurator.Boot/appsettings.json`: удалены две obsolete записи без изменения физических адресов оставшихся точек.
- `Configurator.Infrastructure/Services/AppConfigService.cs`: резервная копия пользовательского файла перед точечной очисткой; сохранение неизвестных полей Modbus и оставшихся DataMap/AlarmMap/AddressLabels при повторных сохранениях.
- `Configurator.Desktop/Workspace/ModbusProfile/ModbusTcpProfileTransferService.cs`: исторический профиль с exact mode IDs получает диагностический отказ на import/export.
- `Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs`, `RouteMapSignalRuntimeAndMappingTests.cs`: legacy совместимость, инертные команды, inventory и сохранение карты.
- `Configurator.Tests.RouteMap.Ui/RouteMapSettingsDialogVisualTests.cs`: user/admin TopBar, отсутствие mode controls/gap и настройки.
- `Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.cs`, `ModbusTcpProfileTransferServiceTests.cs`: backup, неизвестные поля, повторный save, старый профиль.
- `README.md`, `AGENTS.md`; `AgentDocs/ru/{01,02,03,04,05,06}-*.md` и `AgentDocs/en/{01,02,03,04,05,06}-*.md`: актуальные контракты Power/Start/Stop, скрытые режимы, миграция, диагностика и матрица проверок. Исторические `AgentDocs/source/ru` не изменены. `00-master` и `11-code-map` проверены; новые владельцы классов не появились.

## 4. Работа субагентов

| Роль | Задача и результат | Изменённые файлы | Проблемы |
|---|---|---|---|
| `explorer` | Read-only проверка Gate 2/3 | нет | блокеров не найдено; тесты агентом не запускались |
| `protocol_guard` | Read-only анализ cleanup и точечная проверка реализации | нет | нашёл потерю unknown-полей и race save/apply; исправлено оркестратором |
| `avalonia_ui` | Скрытие TopBar и редакторов, layout | три AXAML/code-behind файла | требовались новые headless assertions; добавлены tester |
| `implementer` | Инертные команды VM, settings и inventory | три C# файла | старые тесты ожидали активные mode commands; обновлены tester |
| `tester` | Unit, headless UI, Modbus-конфигурация и профиль | пять тестовых файлов | начальный UI assertion скорректирован; профильные тесты PASS |

## 5. Результаты тестирования

| Тест | Команда | Статус | Примечание |
|---|---|---|---|
| Unit | `dotnet test Configurator.Tests.Unit/Configurator.Tests.Unit.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` | PASS | 247/247 |
| Modbus | `dotnet test Configurator.Infrastructure.Modbus.Tests/Configurator.Infrastructure.Modbus.Tests.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` | PASS | 145/145, mocks/loopback |
| Headless UI | `dotnet test Configurator.Tests.RouteMap.Ui/Configurator.Tests.RouteMap.Ui.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` | PASS | 50/50 |
| Миграции, mapping, backup/profile | профильные фильтры `RouteMapConfigurationTests|RouteMapSignalRuntimeAndMappingTests`, `AppConfigServiceTests|ModbusTcpProfileTransferServiceTests` | PASS | 98/98 и 15/15; дополнительный AppConfig тест 8/8 |
| Полная сборка | `dotnet build DesktopTemplate.slnx --no-restore -v:q` | PASS | выполнена вне sandbox; 0 ошибок |
| Полный solution test | `dotnet test DesktopTemplate.slnx --no-restore -v:q` | PASS | вне sandbox: 247 Unit + 145 Modbus + 50 UI + 35 OPC UA = 477 |
| Документация | `python tools/docs/test_check_markdown_links.py` | PASS | 4 проверки |
| Diff | `git diff --check` | PASS | замечаний по пробелам нет; Git сообщает только о будущей нормализации LF/CRLF |
| Ручная визуальная проверка Windows/ALT Linux | не запускалась | NOT RUN | отдельный установленный стенд не использовался |
| Реальный ПР103 | не запускался | NOT RUN | нет разрешения и безопасной среды |

Первый sandbox-запуск полного suite дал 7 FAIL в OPC UA из-за недоступного пути к NuGet/ReactiveUI; один точечный тест и затем весь suite PASS вне sandbox. Sandbox-сборка без override также не смогла читать каталог Avalonia Licensing; полный build PASS вне sandbox. `-p:UsedAvaloniaProducts=` отключал только задачу статистики Avalonia в профильных sandbox-проверках.

## 6. Проверка совместимости

- JSON v16 и более ранние документы проходят существующую миграцию в schema 17; legacy `Automatic/Manual` остаются инертными DTO. Проверены round-trip, сохранение и повторная миграция unit-тестами.
- Новые defaults не содержат exact obsolete IDs. Сохранённый пользовательский `Modbus.DataMap` очищается явным Save при остановленных ролях, с резервной копией существующего файла. Остальные `system.*`, UnitId, адреса, AlarmMap и пользовательские поля остаются; повторный Save не теряет неизвестные поля.
- Save/Reload mapping не восстанавливает legacy TopBar строки. Если другой активный binding использует obsolete exact ID, сохранение отказывается менять карту и показывает причину.
- Импорт исторического Modbus-профиля с этими IDs отклоняется; экспорт старой сохранённой карты просит предварительную очистку. Route Map JSON import/export сохраняет исторические поля без активации команд.
- `system.reset`, `system.emergency`, `system.fault`, `connection.connected/status` не удалялись. `ModbusDemo.DataMap`, `Modbus.AlarmMap` и физические адреса действующих точек не менялись.

## 7. Остаточные риски

- **P2:** Если роль Modbus запустится во время асинхронного сохранения cleanup, runtime может работать со старой картой до перезапуска роли. Очистка не вызывает `ApplyDataMap` в этом случае; UI сообщает о необходимости перезапуска.
- **P2:** Питание `equip.bucket.power` остаётся unknown, пока физический read-only адрес не сопоставлен по проверенному PLC-контракту. Безопасность и ограничение длительности импульса обеспечиваются PLC; тесты не доказывают поведение реального ПР103 или второго writer того же HR.
- **P2:** Ручная проверка расположения TopBar на целевых Windows/ALT Linux и смене разрешения не выполнялась; headless layout assertions пройдены.
- Неразрешённых P0/P1 по этапам 4–5 не обнаружено.

## 8. Промежуточная приёмка

**Gate 4: PASS.** Режимы скрыты в user/admin, командные пути инертны, их строки отсутствуют в активном inventory/defaults, точечная очистка/backup и конфликт проверены тестами. СБРОС и АВАРИЯ остаются активными, весь UI/Modbus/unit suite пройден.

**Gate 5: PASS.** Полная сборка и 477 тестов PASS, Markdown links PASS, актуальные RU/EN руководства согласованы с кодом; поиск устаревших активных утверждений выполнен.

## 9. Готовность к чату D

Независимую финальную приёмку можно начинать в отдельном чате D. Этап 6 и независимый reviewer в этом чате не запускались. Проверить в D в первую очередь race начала роли во время cleanup, сохранение custom JSON в живом пользовательском профиле, поведение TopBar на целевых платформах и PLC-контракт Pulse/feedback на разрешённом стенде. Коммит, push, merge, installer/RPM и внешние промышленные подключения не выполнялись.
