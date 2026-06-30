# RouteMap guide

RouteMap — первая вкладка Workspace и операторская мнемосхема маршрута. Она хранит
топологию, визуальные параметры и bindings в RouteMap definition, но не хранит
физические Modbus-адреса.

Пользовательский RouteMap definition хранится общим файлом
`%LOCALAPPDATA%\Configurator\RouteMap\route-map.json`. Runtime-выбор источника
`RouteMapRuntime.SignalSource` сохраняется в общем
`%LOCALAPPDATA%\Configurator\appsettings.json`, поэтому настройка mock/Modbus,
сделанная в admin, применяется и при следующем запуске в user.

## Основные файлы

| Область | Где лежит |
|---|---|
| Экран | `Configurator.Desktop/Workspace/RouteMap/RouteMapDashboardView.axaml` |
| Рисование карты | `Workspace/RouteMap/Controls/RouteMapControl.cs` |
| Geometry линий | `Workspace/RouteMap/Controls/RouteSegmentGeometry.cs` |
| Attached cards | `Workspace/RouteMap/Panels/RouteMapAttachedCardsLayer.cs` |
| Правая панель уведомлений | `Workspace/RouteMap/Panels/NotificationsPanelView.axaml` |
| Definition/seed | `Workspace/RouteMap/Models/RouteMapDefinition.cs`, `RouteMapSeed.cs` |
| Runtime mapping | `Workspace/RouteMap/Services/RouteMapRuntimeMapper.cs` |
| Settings dialog | `Workspace/RouteMap/Settings/*` |
| JSON configuration | `Workspace/RouteMap/Configuration/*` |

## Definition

`RouteMapDefinition` описывает:

- логические размеры карты;
- цепочки маршрута;
- узлы;
- сегменты;
- attached-карточки оборудования;
- legacy-заявки и шаблоны заявок из seed;
- display settings и placeholder rules.

Актуальный пользовательский JSON имеет `schemaVersion = 11`:

```json
{
  "schemaVersion": 11,
  "map": {},
  "topBar": {},
  "chains": [],
  "nodes": [],
  "segments": [],
  "cards": [],
  "placeholderRules": []
}
```

JSON не сериализует Avalonia-типы. Цвета хранятся строками `#RRGGBB` или `#AARRGGBB`,
enum — строками, размеры и отступы — собственными DTO.

## Узлы, линии и карточки

- Узлы создаются с `id`, `title`, координатами, kind, label placement, menu kind,
  static visibility и bindings.
- Линии ссылаются на `fromNodeId` и `toNodeId`; поддержаны `Straight` и `RoundedElbow90`.
- Длинные линии могут иметь `ActiveRouteFragment` bindings вида
  `route.<segmentId>.fragment_<n>.active`.
- Карточки оборудования содержат status text, start/stop commands, параметры
  оборудования, стили и привязку к цепочке или vertical anchor.

Линии не selectable: hit-test возвращает узлы и runtime-объекты, но не сегменты.
Не добавляйте selection для линий без отдельного изменения hit-test, marker rendering и тестов.

## Runtime state

RouteMapRuntimeMapper применяет сигналы к объектам. Приоритет состояния:

```text
Offline -> Fault -> ActiveRoute -> static fallback
```

PLC-mapped `system.fault=true` переводит все runtime-объекты RouteMap в тот же `Fault`-вид,
что и локальная роль `Fault=true`; `Offline`/bad quality остаются выше по приоритету.

`Visible=false` скрывает объект. `Fault=true` перекрывает active route. Bad quality или
stale по активному сигналу переводят объект в `Offline`.

Системный `connection.connected=false` означает недоступную Modbus-связь: mapper
форсирует `Offline` для всех узлов и линий, а карточки показывают текст `Не в сети`
серым индикатором и блокируют команды независимо от status/text binding.
Выбранные роли `IsTarget`/`IsLoader` продолжают визуально выделять узел до снятия роли.

## Команды

`ПУСК`, `СТОП`, `АВАРИЯ`, `АВТОМАТ`, `РУЧНОЙ`, `TargetCommand` и `LoaderCommand` работают
как toggle/readback-команды. UI пишет `true` при включении и `false` при снятии или
переключении. PLC должен вернуть readback, чтобы состояние UI стало окончательным.

`ПУСК` и `СТОП` на карточках взаимоисключающие: включение `ПУСК` сначала пишет
`StopCommand=false`, затем `StartCommand=true`; включение `СТОП` сначала пишет
`StartCommand=false`, затем `StopCommand=true`. Если snapshot вернул оба command-бита
`true`, UI показывает включенным только `СТОП`.

`StartOffFeedback` и `StopOffFeedback` снова являются активными ролями карточек. Это
`Read/Bool` сигналы: `true` отключает соответствующую кнопку и визуально снимает
`IsChecked=false`, не выполняя обратную запись в PLC. Остальные `*OffFeedback` и `State`
остаются legacy.

Кнопка `Н` в заголовке карточки открывает модальный диалог параметров оборудования.
Параметры описаны в карточке как `EquipmentParameter`: `Title`, `SignalId`,
`Direction`, `ValueType`. При открытии диалога `Read` и `ReadWrite` параметры получают
последние хорошие значения из runtime snapshot; `Write` параметры открываются пустыми.
Bool-параметры вводятся переключателем `Вкл/Выкл`; числовые и строковые параметры
валидируются по `ValueType`. `Сохранить` перед отправкой проверяет Modbus mapping для
текущего `SignalId`, отправляет `Write`/`ReadWrite` строки через обычный
`SignalWriteRequest`, но не закрывает диалог.

## Правая панель

RouteMap справа показывает `NotificationsPanelView`: вкладка `Уведомления` держит
минимальную ширину `400`, а при открытии `Истории` правая колонка расширяется по ширине
таблицы журнала.
Вкладка `Уведомления` отображает тревоги из `Modbus.AlarmMap` после показа диалога:
один элемент на `AlarmMap.Id`, дата/время берутся с фронта `Alarm=false -> true`.
Элемент повторяет стиль диалога, открывает диалог по нажатию, а кнопка `X` удаляет его
только когда текущий alarm-бит уже `false`. Нижняя кнопка `Очистить список` применяет
то же правило ко всем элементам: активные тревоги остаются в списке.

Вкладка `История` — сессионный in-memory журнал. Она пишет успешные команды `SignalId`,
первые и измененные полученные значения из `Modbus.DataMap`, а также активацию, снятие и
`OK` тревог. Направление показывается стрелкой: `↓` для полученных/входящих событий,
`↑` для отправленных команд и `OK`. `Роли` и `Объекты` отображаются одним столбцом как в
`SignalId ↔ Modbus`; `Адрес` показывает только zero-based offset без имени area.
Операторские тревоги не становятся `SignalId` и не переносятся в `Modbus.DataMap`.

## Редактор

Кнопка `НАСТРОЙКИ` открывает `RouteMapSettingsDialog`. В режиме
`Application.WorkMode=user` кнопка скрыта, а Workspace показывает только RouteMap.
При этом скрытие настроек не меняет сохраненную конфигурацию: user продолжает читать
общие `RouteMapRuntime`, `Modbus` и `ModbusDemo`, но не показывает вкладки настройки,
включая admin-вкладку `Менеджер тревог`. User-режим при этом продолжает реагировать на
`Modbus.AlarmMap`.
В этой карте `Alarm area/Offset/Bit` задают входной бит диалога, `OK area/Offset/Bit` —
отдельный бит подтверждения, `Repeat ms` — повтор при активном alarm-бите, `Pulse ms` —
длительность acknowledgement-импульса.
Вкладки:

- `Источник данных`;
- `Карта и маршруты`;
- `TopBar`;
- `Узлы`;
- `Линии`;
- `Карточки`;
- `Заглушки`.

Во вкладке `Карточки` администратор настраивает список параметров оборудования:
добавляет/удаляет строки, задает название, единственную роль `EquipmentParameter`,
`SignalId`, `Direction` и `ValueType`. Эти параметры остаются доменными `SignalId` и
автоматически попадают в список `SignalId ↔ Modbus`; физические адреса задаются только
там, через `Modbus.DataMap`.

`ПРИМЕНИТЬ` валидирует и публикует definition без записи файла. `СОХРАНИТЬ` валидирует,
атомарно сохраняет JSON и публикует definition. Невалидный документ не публикуется.

## Миграции и validation

Перед validation документ проходит `RouteMapConfigurationMigrator`. Validator проверяет
schema version, уникальность ID, ссылки, роли bindings, обязательные команды, параметры
карточек, геометрию, цвета, fragment bindings, placeholder rules и toggle-семантику
команд.

Не обходите manager и validator прямыми изменениями UI state.
