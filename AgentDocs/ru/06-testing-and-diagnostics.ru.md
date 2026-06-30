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
| Все stale/offline | Состояние `ModbusDemo`, poll interval и `StaleAfterMs` |
| В user-режиме видны вкладки | `Application.WorkMode` и binding `WorkspaceView.IsUserMode` |
| Offline lock не сработал | Системный `connection.connected`, mapper и `AreCommandsEnabled` |
| Общая авария не окрашивает карту | Точка `Modbus.DataMap` с `Name=system.fault`, `Read/Bool`, good quality и значение `true` |
| `ПУСК`/`СТОП` не отключается по PLC | Роли `StartOffFeedback`/`StopOffFeedback`, направление `Read`, тип `Bool`, значение `true` |
| `ПУСК` и `СТОП` одновременно checked | Readback `StartCommand`/`StopCommand`; при конфликте UI должен показывать checked только `СТОП` |
| Параметр карточки не появился в mapping | Настройка добавлена во вкладке `Карточки`, роль `EquipmentParameter`, непустой `SignalId`, применена RouteMap definition |
| `Н` не отправляет значение | У параметра направление `Write`/`ReadWrite`, валидный тип значения, есть точка `Modbus.DataMap` с совместимым access |
| `Н` показывает `SignalId ... не настроен` | Создайте/сохраните строку параметра во вкладке `SignalId ↔ Modbus`; Bool вводится переключателем, но mapping всё равно обязателен для Modbus |
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
5. Настроить endpoint и start addresses в `Modbus Demo`.
6. Проверить, что `RouteMapRuntime`, `Modbus` и `ModbusDemo` сохранены в общем `%LOCALAPPDATA%\Configurator\appsettings.json`.
7. Заполнить `Modbus.DataMap` во вкладке `SignalId ↔ Modbus`.
8. Заполнить `Modbus.AlarmMap` во вкладке `Менеджер тревог`, не дублируя тревоги в `DataMap`.
9. Устранить все `Не настроен` и `Ошибка`.
10. Сначала включить read-only сигналы.
11. Проверить quality, stale, reconnect.
12. Проверить `connection.connected=false`: команды заблокированы, узлы/линии offline, карточки показывают `Не в сети`.
13. Проверить active nodes, active lines и fragments.
14. Проверить modes, emergency, loader/target.
15. Проверить диалоги аварии/повторного подтверждения/обычного сообщения и acknowledgement-импульс.
16. Проверить панель `Уведомления`: элемент появляется после диалога, `Хорошо` снимает unread, `X` и `Очистить список` удаляют только после `Alarm=false`.
17. Проверить вкладку `История`: панель расширяется, команды/received SignalId/тревоги появляются со стрелками направления, объединенным столбцом `Роли / объекты` и числовым offset-адресом.
18. Проверить `system.fault=true`: RouteMap переходит в общий аварийный вид, `false` возвращает обычную per-object логику.
19. По одной разрешить команды оборудования; проверить взаимоисключение `ПУСК`/`СТОП` и `StartOffFeedback`/`StopOffFeedback`.
20. Проверить параметры карточек: кнопка `Н`, Bool-переключатель, валидацию чисел, read-only строки, сохранение `Write`/`ReadWrite`, авто-строки `EquipmentParameter` в `SignalId ↔ Modbus`.
21. Проверить latched/pulse, timeout и потерю связи во время записи.
22. Убедиться, что interlock и safety реализованы в PLC.

