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

Актуальный пользовательский JSON имеет `schemaVersion = 17`:

```json
{
  "schemaVersion": 17,
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

Цвета операторских кнопок TopBar и индикатора PowerStandby/кнопок `ПУСК`/`СТОП` карточки не входят в definition:
они фиксированы в UI для состояний normal, hover, pressed и checked. В JSON остаются
тексты кнопок, bindings и стили самой карточки.

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
Offline -> Disabled -> Fault -> ActiveRoute -> static fallback
```

PLC-mapped `system.fault=true` переводит все runtime-объекты RouteMap в тот же `Fault`-вид,
что и локальная роль `Fault=true`; `Offline`/bad quality остаются выше по приоритету.

`Visible=false` скрывает объект. `Fault=true` перекрывает active route. Bad quality или
stale по активному сигналу переводят объект в `Offline`.

Опциональная роль `Enabled` доступна для действующих кнопок TopBar (`СБРОС`, `АВАРИЯ`), узла, линии и карточки.
Это `Read/Bool`: хорошее значение `false` дает состояние `Disabled`, блокирует ввод и
использует темно-серый `Map.Palette.Disabled`; отсутствие binding или значения оставляет
элемент доступным. Bad/stale сохраняет обычное состояние `Offline`.
Для карточки `Enabled=false` отключает визуал, `ПУСК` и `СТОП`, но не управляет
кнопкой `Н`: она доступна без подключения в обоих режимах. Для карточки с
`PowerFeedback` индикатор питания остаётся только отображением; `Enabled=false`
не порождает selector-записей.

Системный `connection.connected=false` означает недоступную Modbus-связь: mapper
форсирует `Offline` для всех узлов и линий, а карточки показывают текст `Не в сети`
серым индикатором и блокируют команды независимо от status/text binding. Кнопка `Н`
остается доступной: editable-значения можно сохранить локально и без PLC.
Выбранные роли `IsTarget`/`IsLoader` продолжают визуально выделять узел до снятия роли.

## Команды

Карточка «Кюбель Л.К.» использует `equip.bucket.power` (`PowerFeedback`,
`Read/Bool`), `equip.bucket.start` (`StartCommand`, `ReadWrite/Bool/Pulse`) и
`equip.bucket.stop` (`StopCommand`, `ReadWrite/Bool/Latched`). PowerStandby размером
40×40 — неинтерактивный индикатор: хороший свежий `false` даёт красный `#D10000`,
`true` — зелёный `#00D107`; missing/Bad/Stale/offline или неверный тип дают
нейтральный серый и подсказку о неизвестном состоянии. Клик, клавиши, readback и
`Enabled=false` не отправляют запись от индикатора. Адрес питания задаётся вручную:
старый writable selector не является доказанным физическим feedback.

`ПУСК` — моментальная `Button`: одно действие передаёт только `StartCommand=true`.
Modbus dispatcher выполняет один Pulse `true → delay → false` с настроенным
`PulseDurationMs` (default 300 мс). Отпускание указателя не пишет `false`;
повторное нажатие не накладывает второй импульс. При неопределённом исходе `true`
делается ограниченная попытка `false`, ошибка видна оператору. `ПУСК` не снимает
`СТОП`; факт работы привода определяется отдельным PLC feedback.
Если сохранённый `Modbus.DataMap` задаёт Start не как Pulse, команда отклоняется до записи;
оператор видит ошибку и должен исправить mapping.

`СТОП` остаётся `ToggleButton`: checked пишет только `StopCommand=true`, unchecked —
только `StopCommand=false`. Финальное положение берётся из хорошего свежего readback;
snapshot не инициирует обратную запись. Ошибка записи/подтверждения отображается.
Если свежий readback не подтверждает запрошенное положение за 10 секунд, ожидание
снимается с видимой ошибкой; автоматического повтора записи нет. Stop с режимом
записи не Latched отклоняется до записи.
Это обычная программная команда, не аварийный останов и не PLC interlock.
`АВАРИЯ`, `TargetCommand` и `LoaderCommand` сохраняют прежнюю семантику.

`СБРОС` — отдельная моментальная команда `system.reset` с ролью `ResetCommand`:
`ReadWrite/Bool`, режим записи Modbus `Pulse`, не `Latched`. При нажатии UI отправляет
только `true` и не пишет `false` при отпускании. Modbus dispatcher завершает импульс
записью `false` после `PulseDurationMs`. Кнопка временно показывает активное состояние
во время отправки и учитывает последующий readback; подробности — в [гайде Modbus TCP](03-modbus-tcp-guide.ru.md).

Исторические карточки без `PowerFeedback` сохраняют прежнюю selector-семантику для
совместимости JSON; новые и мигрированные карточки «Кюбель Л.К.» её не используют.
Режимы `АВТОМАТ` и `РУЧНОЙ` скрыты в user/admin TopBar и редакторе настроек, их
команды инертны, а SignalId исключены из активного inventory. Legacy-поля TopBar
сохраняются при чтении и сохранении старого JSON.

`StartOffFeedback` и `StopOffFeedback` снова являются активными ролями карточек. Это
`Read/Bool` сигналы: `true` отключает соответствующую кнопку; для исторических
Toggle-карточек также снимает checked без обратной записи в PLC. Остальные `*OffFeedback` и `State`
остаются legacy.

Кнопка `Н` в заголовке карточки открывает модальный диалог параметров оборудования.
Ее доступность не зависит от binding-регистра `Enabled` и от подключения в обоих режимах.
Параметры описаны в карточке как `EquipmentParameter`: `Title`, `SignalId`,
`Direction`, `ValueType`, а editable-параметры также хранят локальный setpoint,
статус ожидающей автоотправки и последнюю ошибку. При открытии `Read` получает последнее
хорошее runtime-значение, а `Write`/`ReadWrite` используют сохранённый setpoint, если он
есть.
Bool-параметры вводятся переключателем `Вкл/Выкл`; числовые и строковые параметры
валидируются по `ValueType`, включая `WORD`, `DWORD` и `DATE`. В admin-режиме диалог
показывает техническую подпись `SignalId • Type`; в user-режиме она скрыта. `Сохранить`
без подключения сохраняет валидные `Write`/`ReadWrite` значения в `route-map.json` без
проверки Modbus mapping и помечает их ожидающими. При первом reconnect Modbus каждое
ожидающее значение отправляется один раз; успех или ошибка снимают ожидание, а ошибка
видна в диалоге. Онлайн-сохранение сохраняет значение и использует обычную проверку
mapping/`SignalWriteRequest`; ручное `Сохранить` создаёт новую попытку.

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
`SignalId`, `Direction` и `ValueType`. Для editable-строк RouteMap также сохраняет
последний локальный setpoint, pending-статус и ошибку отправки. Изменение `SignalId`,
`Direction` или `ValueType` очищает эти данные; дублирование карточки их не копирует.
Эти параметры остаются доменными `SignalId` и
автоматически попадают в список `SignalId ↔ Modbus`; физические адреса задаются только
там, через `Modbus.DataMap`. Новые параметры по умолчанию получают тип `Word`; старые
`UInt16` остаются валидными и совместимыми.

Новые и мигрированные карточки с моментальным Start содержат `PowerFeedback` с
`<cardId>.power` без автоматического назначения физического адреса. Старые selector
bindings не возвращаются для «Кюбель Л.К.». Миграция `v12 -> v13` добавляет TopBar
`СБРОС` с обязательным `ResetCommand` и SignalId `system.reset`. `Enabled` остается
опциональным и добавляется администратором. На действующих кнопках TopBar
опциональный `Enabled` можно добавлять и удалять, а основной command-binding
остаётся обязательным. Исторические `Automatic/Manual` редактированию не подлежат.

`ПРИМЕНИТЬ` валидирует и публикует definition без записи файла. `СОХРАНИТЬ` валидирует,
атомарно сохраняет JSON и публикует definition. Невалидный документ не публикуется.

## Миграции и validation

Перед validation документ проходит `RouteMapConfigurationMigrator`. Validator проверяет
schema version, уникальность ID, ссылки, роли bindings, обязательные команды, параметры
карточек, геометрию, цвета, fragment bindings, placeholder rules и toggle-семантику
команд.

Не обходите manager и validator прямыми изменениями UI state.
