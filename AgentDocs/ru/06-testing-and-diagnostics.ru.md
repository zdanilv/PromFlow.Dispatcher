# Проверка и диагностика

Используйте этот документ перед PR, commit или передачей работы другому агенту.
Выбирайте узкий тест по [карте кода](11-code-map.ru.md), затем расширяйте проверку по риску.
Ни одна команда ниже не должна обращаться к внешнему PLC или промышленной сети.

## Базовые проверки

Команды выполняйте из корня репозитория. Для Windows используйте PowerShell:

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

На Linux используйте Bash и пути с `/`, например
`dotnet test ./DesktopTemplate.slnx --no-restore`. Restore нужен один раз лишь при
отсутствии assets или изменении зависимостей. Сборка подтверждает компиляцию, но не
заменяет unit, integration и UI/headless проверки. Полный solution test нужен для
изменений между слоями, схемы конфигурации, протокола, packaging и перед merge.

Для правок только Markdown: проверьте факты по коду/тестам, RU/EN согласованность,
относительные ссылки и существование названных файлов/классов; затем выполните
`git diff --check` и `git status --short`. Проверяйте якоря отдельно, если checker
поддерживает Unicode и повторяющиеся заголовки. Сборка и тесты не требуются, когда код,
проектные файлы и конфигурация не менялись. Для solution/project metadata сборка обязательна.

Если build заблокирован running `.NET Host`, остановите запущенное приложение или debug
session и повторите build.

### Проверка документации

Из корня репозитория с Python 3:

```bash
python tools/docs/check_markdown_links.py
```

Checker без сетевых запросов проверяет существование локальных файлов, каталогов и
изображений в `README.md`, `AGENTS.md`, `AgentDocs/ru/**/*.md`,
`AgentDocs/en/**/*.md`, `AgentDocs/source/ru/**/*.md` и `docs/plans/**/*.md`.
Исторические материалы включены на тех же условиях; внешний HTTP(S) игнорируется.
Код возврата `0` означает отсутствие обнаруженных ошибок, `1` — сломанные пути,
`2` — ошибку чтения/выполнения. Диагностика содержит исходный файл, строку и ссылку.
Исправьте путь или удалите неверную ссылку в исходном Markdown и повторите команду;
checker сам документы не меняет. Для проверки инструмента:
`python -B -m unittest discover -s tools/docs -p 'test_*.py' -v`.

Поддерживаются простые однострочные inline-ссылки и полные/сокращённые reference-ссылки
`[текст][id]`/`[id][]`. Якоря **не проверяются**, даже если файл существует;
вложенные/экранированные квадратные скобки в подписи, shortcut reference `[id]`,
многострочные ссылки и HTML-ссылки также вне охвата.
Отдельно проверяйте такие ссылки и смысл технических утверждений. В репозитории
нет GitHub Actions workflow для этой проверки; запускайте её локально перед PR.

Для согласованности контрактов после checker выполните короткую ручную сверку:

1. Сравните `CurrentSchemaVersion` в `RouteMapConfigurationDocument.cs` с
   конкретными `schemaVersion` в актуальных `02-route-map-guide` RU/EN;
   исторические примеры миграций не приравнивайте к текущей схеме.
2. Сверьте четыре `TabItem Header` в `WorkspaceView.axaml` с README, RU/EN
   master-индексами и гайдами. `ModbusDemo` — внутренний идентификатор,
   отображаемая вкладка — `Modbus TCP`.
3. В README и `08-build-and-install.ru.md` проверьте команды установки RPM:
   после `su -` путь к RPM должен быть доступен новой shell-среде; переменная,
   объявленная только до `su -`, не переносится автоматически.
4. Сверьте заявления об OPC UA в README, архитектуре и `10-opc-ua-guide.ru.md`
   с `WorkspaceView.axaml`: наличие классов OPC UA не доказывает вкладку.
5. Проверьте имена классов/тестов в `11-code-map.ru.md` по исходникам и тестам.
   Checker проверяет цели Markdown-ссылок, но не существование C#-символов
   и не все пути, записанные в моноширинном тексте.

## RouteMap тесты

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap"
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus тесты

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

## Матрица минимальных проверок для изменений продукта

В примерах `--filter "FullyQualifiedName~ИмяКласса"` запускает тесты указанного класса.
Выбирайте конкретное имя из таблицы; если изменение затрагивает несколько строк,
объединяйте их проверки. `Configurator.Tests.Unit` — unit, `Configurator.Tests.RouteMap.Ui`
— headless UI, `Infrastructure.*.Tests` — unit и локальные integration/loopback сценарии.

| Изменённая область | Минимальная проверка | Когда расширять проверку |
|---|---|---|
| Только Markdown | Проверка ссылок и фактов по исходникам, `git diff --check`, `git status --short` | Если изменены команды/пути документации — проверить их на подходящей платформе; если затронута project metadata — сборка |
| Route Map Configuration | Unit `RouteMapConfigurationTests`: round-trip, migration, validation | При schema/migration/import/export — все RouteMap unit и UI/headless, затем полный suite перед merge. Отдельного подтверждённого теста import/export в текущей карте кода нет: зафиксировать пробел |
| Route Map UI и геометрия | Unit `RouteSegmentGeometryTests`, `RouteMapViewportLayoutTests` или тест затронутой ViewModel; headless `RouteMapSettingsDialogVisualTests` | При AXAML/layout или общих стилях — весь UI-проект и ручная проверка рендера на целевой платформе; headless не доказывает внешний вид на всех ОС |
| SignalId и Modbus mapping | Unit `RouteMapSignalRuntimeAndMappingTests`; Modbus `ModbusDataMapValidatorTests` и `RouteMapModbusAdapterTests` | При изменении адресации, типов или записи — весь Modbus-проект и RouteMap unit/UI |
| Modbus TCP runtime | `ModbusTcpServiceTests` и `ModbusRuntimeSmokeTests` на mock/loopback | При изменении lifecycle, reconnect или client/server ролей — весь Modbus-проект и полный suite |
| Register-bit RMW | `ModbusTcpServiceTests` (соседние биты и конкурентные записи), `ModbusBitWriterTests` (pulse/RMW) | При изменении write path — loopback integration, полный suite и независимый review |
| PLC-команды, readback и pulse | Unit `RouteMapRuntimeMapperTests`, `RouteNodeRoleStateTransitionsTests`, `EquipmentCardViewModelTests`; Modbus `RouteMapModbusAdapterTests`, `ModbusTcpServiceTests` | При изменении порядка записей, reset/selector, таймаута, отмены или readback — все RouteMap/Modbus tests, loopback и независимый review; не повторять автоматически неидемпотентную команду |
| Alarm/ACK | Unit `ModbusAlarmMonitorTests`, `RouteMapSessionJournalTests`; Modbus `ModbusAlarmMapValidatorTests`, `ModbusBitWriterTests` | При изменении ACK pulse, repeat/clear или адресации — весь Modbus-проект, затронутые UI/headless тесты, loopback и независимый review |
| OPC UA client/server/security | `OpcUaOptionsTests`, `OpcUaRuntimeTests`, `OpcUaTagWriteRequestValidatorTests`; локальный `OpcUaIntegrationTests` для протокольного изменения | При lifecycle, subscription, auth/security или DI изменениях — весь OPC UA-проект, полный suite и независимый review; integration требует свободный loopback порт и не проверяет внешний endpoint |
| Конфигурация и DI | `AppConfigServiceTests`, `ApplicationOptionsTests`, `WorkspaceViewModelTests` | При путях, overlay, формате, миграции или регистрации сервисов — сборка и полный suite; для RouteMap добавить `RouteMapConfigurationTests` |
| Windows / ALT Linux packaging | Сборка solution и read-only проверка скриптов, manifest, ресурсов и путей по [08](08-build-and-install.ru.md) | При изменении пакета — полный suite и platform smoke на соответствующей ОС/стенде; установку и запуск installer/RPM согласовать отдельно |

Пример точечного запуска в PowerShell:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMapConfigurationTests"
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter "FullyQualifiedName~ModbusTcpServiceTests"
dotnet test .\Configurator.Infrastructure.OpcUa.Tests\Configurator.Infrastructure.OpcUa.Tests.csproj --no-restore --filter "FullyQualifiedName~OpcUaRuntimeTests"
```

Для RouteMap unit набора используйте существующую фильтрацию по имени:
`dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap"`;
отдельно запускайте `EquipmentCardViewModelTests`, если изменена карточка.

**Независимый review обязателен** для Modbus/OPC UA write/lifecycle/security, PLC-команд,
RMW, ACK, schema migration и изменений между слоями. Reviewer сверяет порядок операций,
отмену, loss of connection, readback, совместимость старых конфигураций и границы safety;
локальные тесты не заменяют такой разбор. Промышленная безопасность и interlock остаются в PLC.

**Статусы отчёта:** `PASS` — команда реально завершилась успешно; `FAIL` — запуск
завершился ошибкой (приложить воспроизводящий вывод); `NOT RUN` — проверка не запускалась
или неприменима, с причиной; `BLOCKED` — запуск требовался, но невозможен из-за конкретной
среды/зависимости, с указанием блокера. Не выдавайте `BLOCKED` за `PASS`.

## Диагностика SignalId ↔ Modbus

| Симптом | Проверить |
|---|---|
| `Не настроен` | Нет точки `Modbus.DataMap` с `Name == SignalId` |
| Ошибка типа | `SignalValueType` и Modbus `Type` |
| Ошибка доступа | Binding direction и point access |
| Конфликт адреса | Duplicate coil/bit или пересекающиеся registers |
| UI остается в mock | `RouteMapRuntime.SignalSource` и флаг источника данных |
| User сбрасывает admin-настройки | Общий `%LOCALAPPDATA%\Configurator\appsettings.json` и overlay после exe-local defaults |
| Все stale/offline | Состояние `Modbus TCP`, poll interval и `StaleAfterMs` |
| В user-режиме видны вкладки | `Application.WorkMode` и binding `WorkspaceView.IsUserMode` |
| Offline lock не сработал | Системный `connection.connected`, mapper и `AreCommandsEnabled` |
| Общая авария не окрашивает карту | Точка `Modbus.DataMap` с `Name=system.fault`, `Read/Bool`, good quality и значение `true` |
| `ПУСК`/`СТОП` не отключается по PLC | Роли `StartOffFeedback`/`StopOffFeedback`, направление `Read`, тип `Bool`, значение `true` |
| `ПУСК` и `СТОП` одновременно checked | Readback `StartCommand`/`StopCommand`; при конфликте UI должен показывать checked только `СТОП` |
| `С` не переключается | Проверьте обязательные `UncheckedCommand`/`CheckedCommand`, разные SignalId, `ReadWrite/Bool`, Latched и наличие readback |
| `С` показывает неверное состояние | Checked допустим только при `Unchecked=false`, `Checked=true`; конфликт двух `true` отображается unchecked |
| `С` отключилась вместе с карточкой | При good `Enabled=false` карточки должны отключаться визуал, `ПУСК` и `СТОП`; `Н` остаётся доступной offline в обоих режимах, а `С` остается активной при `connection.connected=true` |
| `Н` недоступна или зависит от регистра | `Enabled=false` и `connection.connected=false` не должны отключать `Н` ни в одном режиме |
| Offline setpoint не отправился | Проверьте `route-map.json`: `pendingAutoDispatch=true` до первого Modbus reconnect; после попытки pending снимается, а ошибка остаётся в `lastDispatchError` |
| `С` не сбрасывает selector при disabled | При первом хорошем `Enabled=false` должны уйти две записи: `CheckedCommand=false`, затем `UncheckedCommand=false`; повторный snapshot не дублирует запись |
| `СБРОС` не работает | Проверьте TopBar `ResetCommand`, SignalId `system.reset`, `ReadWrite/Bool`, Pulse, `PulseDurationMs`, readback и optional `Enabled` этой кнопки |
| Элемент не отключается | Роль `Enabled` должна быть `Read/Bool` с good quality и значением `false`; missing оставляет enabled, bad/stale дает Offline |
| Параметр карточки не появился в mapping | Настройка добавлена во вкладке `Карточки`, роль `EquipmentParameter`, непустой `SignalId`, применена RouteMap definition |
| `Н` не отправляет значение online | У параметра направление `Write`/`ReadWrite`, валидный тип значения, есть точка `Modbus.DataMap` с совместимым access; offline-сохранение mapping не требует |
| `Н` показывает `SignalId ... не настроен` | Создайте/сохраните строку параметра во вкладке `SignalId ↔ Modbus`; Bool вводится переключателем, но mapping всё равно обязателен для Modbus |
| `WORD`/`DWORD`/`DATE` не пишется | Проверьте совместимость `Word → Word`, legacy `UInt16 → UInt16`, `Dword → Dword`, `Date → Date`, длину register-точки и writable access |
| `String` из `Н` не пишется | Увеличьте `Length` строки во вкладке `SignalId ↔ Modbus`; емкость равна `Length * 2` UTF-8 байт |
| Bit-write отклонен | Нет первого raw snapshot holding register |
| Нет readback | Access, PLC echo и `WriteConfirmationTimeoutMs` |
| Неверный physical address | StartAddress и 0/1-based notation PLC |

## Диагностика Менеджера тревог

| Симптом | Проверить |
|---|---|
| Диалог не появляется | Открытый Workspace в `admin` или `user`, активный runtime snapshot и `Modbus.AlarmMap[].Enabled` |
| Диалог появляется повторно слишком часто | `RepeatIntervalMs` конкретной тревоги |
| `Хорошо` не подтверждает | Включен `AcknowledgementEnabled`, заданы отдельный `Acknowledgement` address/bit и `AcknowledgementPulseDurationMs` |
| Ошибка адреса в менеджере | Попадание Alarm/Acknowledgement в ranges `ModbusDemo.Client/Server`; выключенный `AcknowledgementEnabled` OK-поля не валидирует |
| RouteMap видит тревогу как SignalId | Тревога ошибочно добавлена в `Modbus.DataMap` вместо `Modbus.AlarmMap` |
| Уведомление не удаляется кнопкой `X` | Текущий alarm-бит еще `true`; удаление разрешено только после `Alarm=false` |
| `Очистить список` оставляет элементы | Эти тревоги все еще активны; массовая очистка использует ту же проверку, что `X` |
| Маркер непрочитанного не снимается | Нажатие `Хорошо` в диалоге тревоги или из элемента уведомления |
| В `Истории` нет SignalId | Точка отсутствует в `Modbus.DataMap`, значение bad/stale или не изменилось с прошлого quality snapshot |
| В `Истории` адрес выглядит как `HoldingRegister 3` | Формат устарел; актуальная колонка `Адрес` показывает только offset `3` |
| Правая панель не расширяется на `Истории` | У `NotificationsPanelView` не должно быть фиксированного `Width/MaxWidth=400` |
| В `Истории` нет выгрузки в БД | Реализован только `NoopSessionJournalExporter`; схема и подключение БД не реализованы |

При проверке таблицы `Менеджер тревог` читайте группы колонок так: `Alarm area/Offset/Bit`
это входной бит показа диалога, `OK feedback` включает отдельный бит подтверждения
`OK area/Offset/Bit`,
`Alarm client/server` и `OK client/server` это физические адреса по базам
`ModbusDemo.Client/Server`. `Repeat ms` должен быть `1000..86400000`, `Pulse ms` —
`1..60000`; для включенного `OK feedback` `HoldingRegister`-`Bit` обязателен в диапазоне `0..15`, для `Coil`
бит должен отсутствовать.

## Production checklist

1. Проверить RouteMap в `Mock`.
2. Зафиксировать список всех SignalId.
3. Получить утвержденную PLC карту coils/registers/bits.
4. Уточнить notation адресов.
5. Настроить endpoint и start addresses во вкладке `Modbus TCP`.
6. Проверить, что `RouteMapRuntime`, `Modbus` и `ModbusDemo` сохранены в общем `%LOCALAPPDATA%\Configurator\appsettings.json`.
7. Заполнить `Modbus.DataMap` во вкладке `SignalId ↔ Modbus`.
8. Заполнить `Modbus.AlarmMap` во вкладке `Менеджер тревог`, не дублируя тревоги в `DataMap`.
9. Устранить все `Не настроен` и `Ошибка`.
10. Сначала включить read-only сигналы.
11. Проверить quality, stale, reconnect.
12. Проверить `connection.connected=false`: команды заблокированы, узлы/линии offline, карточки показывают `Не в сети`; `Н` остаётся доступной для локального сохранения в обоих режимах.
13. Проверить active nodes, active lines и fragments.
14. Проверить modes, reset, emergency, loader/target.
15. Проверить диалоги аварии/повторного подтверждения/обычного сообщения и acknowledgement-импульс.
16. Проверить панель `Уведомления`: элемент появляется после диалога, `Хорошо` снимает unread, `X` и `Очистить список` удаляют только после `Alarm=false`.
17. Проверить вкладку `История`: панель расширяется, команды/received SignalId/тревоги появляются со стрелками направления, объединенным столбцом `Роли / объекты` и числовым offset-адресом.
18. Проверить `system.fault=true`: RouteMap переходит в общий аварийный вид, `false` возвращает обычную per-object логику.
19. По одной разрешить команды оборудования; проверить взаимоисключение `ПУСК`/`СТОП` и `StartOffFeedback`/`StopOffFeedback`.
20. Проверить кнопку `С`: две последовательные записи, readback, конфликт двух `true`, разные Bool-точки, запрет Pulse и reset selector-команд при good `Enabled=false`.
21. Проверить `СБРОС`: положение слева от `АВАРИЯ`, yellow normal state, `system.reset`, readback, Pulse и validation error для Latched.
22. Проверить `Enabled=false` отдельно для каждой TopBar-кнопки, узла, линии и карточки, а также missing/bad/stale.
23. Проверить параметры карточек: `Н` доступна offline в обоих режимах независимо от `Enabled`; проверить локальное сохранение `Write`/`ReadWrite` без DataMap/dispatcher, восстановление после перезапуска, единственную автоотправку всех pending после reconnect, сохранение ошибки без автоповтора и ручную повторную попытку. Также проверить Bool-переключатель, `WORD`/`DWORD`/`DATE`, read-only строки, скрытие `SignalId • Type` в user-режиме и авто-строки `EquipmentParameter` в `SignalId ↔ Modbus`.
24. Проверить latched/pulse, timeout и потерю связи во время записи.
25. Убедиться, что interlock и safety реализованы в PLC.

