# Проверка и диагностика

Используйте этот документ перед PR, commit или передачей работы другому агенту.

## Базовые проверки

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

Для документационных и solution metadata изменений минимум:

```powershell
rg --files -g '*.md'
dotnet build .\DesktopTemplate.slnx --no-restore
git status --short
```

Если build заблокирован running `.NET Host`, остановите запущенное приложение или debug
session и повторите build.

## RouteMap тесты

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Modbus тесты

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
```

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
| `С` отключилась вместе с карточкой | При good `Enabled=false` карточки должны отключаться `Н`, `ПУСК`, `СТОП` и визуал, но `С` остается активной при `connection.connected=true` |
| `С` не сбрасывает selector при disabled | При первом хорошем `Enabled=false` должны уйти две записи: `CheckedCommand=false`, затем `UncheckedCommand=false`; повторный snapshot не дублирует запись |
| `СБРОС` не работает | Проверьте TopBar `ResetCommand`, SignalId `system.reset`, `ReadWrite/Bool`, Pulse, `PulseDurationMs`, readback и optional `Enabled` этой кнопки |
| Элемент не отключается | Роль `Enabled` должна быть `Read/Bool` с good quality и значением `false`; missing оставляет enabled, bad/stale дает Offline |
| Параметр карточки не появился в mapping | Настройка добавлена во вкладке `Карточки`, роль `EquipmentParameter`, непустой `SignalId`, применена RouteMap definition |
| `Н` не отправляет значение | У параметра направление `Write`/`ReadWrite`, валидный тип значения, есть точка `Modbus.DataMap` с совместимым access |
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
| `Хорошо` не подтверждает | Отдельный `Acknowledgement` address/bit и `AcknowledgementPulseDurationMs` |
| Ошибка адреса в менеджере | Попадание Alarm/Acknowledgement в ranges `ModbusDemo.Client/Server` |
| RouteMap видит тревогу как SignalId | Тревога ошибочно добавлена в `Modbus.DataMap` вместо `Modbus.AlarmMap` |
| Уведомление не удаляется кнопкой `X` | Текущий alarm-бит еще `true`; удаление разрешено только после `Alarm=false` |
| `Очистить список` оставляет элементы | Эти тревоги все еще активны; массовая очистка использует ту же проверку, что `X` |
| Маркер непрочитанного не снимается | Нажатие `Хорошо` в диалоге тревоги или из элемента уведомления |
| В `Истории` нет SignalId | Точка отсутствует в `Modbus.DataMap`, значение bad/stale или не изменилось с прошлого quality snapshot |
| В `Истории` адрес выглядит как `HoldingRegister 3` | Формат устарел; актуальная колонка `Адрес` показывает только offset `3` |
| Правая панель не расширяется на `Истории` | У `NotificationsPanelView` не должно быть фиксированного `Width/MaxWidth=400` |
| В `Истории` нет выгрузки в БД | Реализован только `NoopSessionJournalExporter`; схема и подключение БД не реализованы |

При проверке таблицы `Менеджер тревог` читайте группы колонок так: `Alarm area/Offset/Bit`
это входной бит показа диалога, `OK area/Offset/Bit` это отдельный бит подтверждения,
`Alarm client/server` и `OK client/server` это физические адреса по базам
`ModbusDemo.Client/Server`. `Repeat ms` должен быть `1000..86400000`, `Pulse ms` —
`1..60000`; для `HoldingRegister` `Bit` обязателен в диапазоне `0..15`, для `Coil`
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
12. Проверить `connection.connected=false`: команды заблокированы, узлы/линии offline, карточки показывают `Не в сети`.
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
23. Проверить параметры карточек: кнопка `Н`, Bool-переключатель, валидацию `WORD`/`DWORD`/`DATE`, read-only строки, сохранение `Write`/`ReadWrite`, скрытие `SignalId • Type` в user-режиме и авто-строки `EquipmentParameter` в `SignalId ↔ Modbus`.
24. Проверить latched/pulse, timeout и потерю связи во время записи.
25. Убедиться, что interlock и safety реализованы в PLC.

