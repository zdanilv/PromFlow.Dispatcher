# RouteMap и Modbus TCP: полное описание и руководство

## 1. Назначение

Документ описывает фактическую реализацию RouteMap и ее интеграцию с Modbus TCP в
`PromFlow.Dispatcher`, ветка `3-route-map-merge`.

RouteMap является первой вкладкой Workspace и экраном по умолчанию. Она отображает
технологический маршрут, состояние узлов и линий, роли погрузки/назначения, карточки
оборудования, TopBar и команды оператора. Источником данных может быть встроенный Mock
или общий Modbus Demo TCP runtime.

В `Application.WorkMode=admin` Workspace показывает вкладки `Route Map`,
`SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus Demo`. В `Application.WorkMode=user`
остается только RouteMap на всю рабочую область, а кнопка `НАСТРОЙКИ` скрыта. Экран
`Modbus Demo` владеет запуском, остановкой и настройкой TCP endpoint. RouteMap работает
через тот же runtime, но декодирует snapshot по отдельной RouteMap-карте
`Modbus.DataMap`; монитор тревог читает `Modbus.AlarmMap` в admin и user режимах.

`Application.WorkMode` читается из launch-конфига как режим оболочки. Рабочие секции
`RouteMapRuntime`, `Modbus` и `ModbusDemo` накладываются поверх defaults из общего
`%LOCALAPPDATA%\Configurator\appsettings.json`, поэтому admin и user используют одну
конфигурацию оборудования.

UI не является контуром функциональной безопасности. Блокировки, interlock, аварийная
логика и окончательное разрешение исполнительных команд должны оставаться в PLC.

## 2. Быстрый старт

### Запуск без PLC

В `Configurator.Boot/appsettings.json` оставьте:

```json
"RouteMapRuntime": {
  "SignalSource": "Mock",
  "StaleAfterMs": 1500
}
```

Запустите `Configurator.Boot`. После авторизации откроется Workspace с первой вкладкой
`Route Map`. Mock-провайдер формирует значения на основе текущих bindings карты, поэтому
редактор, маршруты и команды можно проверять без Modbus-сервера.

### Запуск с PLC или локальным Modbus-сервером

1. Настройте endpoint и lifecycle в секции `ModbusDemo` или на вкладке `Modbus Demo`.
2. Убедитесь, что `DataMap[].Name` совпадает с `SignalId` RouteMap.
3. Переключите `RouteMapRuntime.SignalSource` в `Modbus`.
4. Заполните RouteMap-карту `Modbus.DataMap` на вкладке `SignalId ↔ Modbus`.
5. Заполните `Modbus.AlarmMap` на вкладке `Менеджер тревог`, если нужны аварии, повторные подтверждения или обычные сообщения.
6. Сначала проверьте read-only сигналы, затем разрешайте команды.

Пример:

```json
"RouteMapRuntime": {
  "SignalSource": "Modbus",
  "StaleAfterMs": 1500
},
"Modbus": {
  "DataMap": [],
  "AlarmMap": []
},
"ModbusDemo": {
  "AutostartOnWorkspaceOpen": true,
  "StartupMode": "Client"
}
```

## 3. Архитектура

### Чтение данных

```text
ModbusDemo Client/Server settings
  -> shared ModbusRuntimeService
  -> RouteMap ModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> Avalonia UI
```

### Запись команд

```text
TopBar / узел / карточка оборудования
  -> SignalWriteRequest
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> RouteMap IModbusTcpService.SetAsync
  -> shared Modbus client или локальный server runtime
  -> обычный poll/readback
```

RouteMap не знает IP-адресов, UnitId, номеров регистров и coils. UI работает только с
доменными `SignalId`, типами и направлениями bindings. Физическая адресация находится в
`Modbus.DataMap`, а endpoint и lifecycle берутся из `ModbusDemo`.

### Выбор реализации через DI

`Configurator.Boot/Program.cs` создает оба backend и единый переключаемый
`IRouteMapSignalRuntime`:

| SignalSource | Активный ISignalValueProvider | Активный IEquipmentCommandDispatcher |
|---|---|---|
| `Mock` | `MockSignalProvider` | `MockEquipmentCommandDispatcher` |
| `Modbus` | `ModbusTcpSignalValueProvider` | `ModbusTcpCommandDispatcher` |

Источник можно горячо переключить в `Route Map` → `НАСТРОЙКИ` → `Источник данных`.
Подписка dashboard сохраняется, а команды направляются только активному backend.
`RouteMapModbusBindingDiagnostics` проверяет соответствие RouteMap и `Modbus.DataMap`,
когда выбран Modbus.

## 4. Расположение кода

| Область | Путь |
|---|---|
| Signal-контракты | `Configurator.Application/Services/Signals` |
| Modbus-контракты и options | `Configurator.Application/Services/Modbus` |
| RouteMap UI и конфигурация | `Configurator.Desktop/Workspace/RouteMap` |
| Modbus adapters RouteMap | `Configurator.Infrastructure.Modbus/RouteMap` |
| Shared Modbus facade/runtime | `Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs` |
| DI и выбор источника | `Configurator.Boot/Program.cs` |
| Runtime-конфигурация | defaults: `Configurator.Boot/appsettings.json`; overrides: `%LOCALAPPDATA%\Configurator\appsettings.json` |
| RouteMap unit-тесты | `Configurator.Tests.Unit/RouteMap` |
| RouteMap headless UI-тесты | `Configurator.Tests.RouteMap.Ui` |
| Modbus-тесты | `Configurator.Infrastructure.Modbus.Tests` |

## 5. Конфигурация RouteMap

### Активный файл

Пользовательская конфигурация хранится в:

```text
%LocalAppData%\Configurator\RouteMap\route-map.json
```

Если файл отсутствует, используется `RouteMapSeed`. Если файл поврежден, несовместим или
не проходит валидацию, приложение продолжает работу с seed. Причина доступна через
`RouteMapConfigurationManager.LastLoadError` и показывается в редакторе.

Запись выполняется атомарно: JSON сначала сохраняется в файл `.tmp`, сбрасывается на диск,
а затем заменяет активный файл.

### Корневая структура JSON

Текущая версия схемы — `16`:

```json
{
  "schemaVersion": 16,
  "map": {},
  "topBar": {},
  "chains": [],
  "nodes": [],
  "segments": [],
  "cards": [],
  "placeholderRules": []
}
```

JSON использует camelCase. Enum сохраняются строками. Avalonia-типы в контракт не входят:
цвета задаются строками `#RRGGBB` или `#AARRGGBB`, отступы и радиусы углов представлены
собственными DTO.

`Requests`, `RequestTemplates` и `Vehicles` не сохраняются в пользовательском профиле и
при mapping берутся из `RouteMapSeed`.

### Раздел map

Основные параметры:

| Поле | Назначение |
|---|---|
| `logicalWidth`, `logicalHeight` | Логическая область масштабирования |
| `mapPadding` | Внутренний отступ карты |
| `cardColumnGap` | Расстояние до колонки карточек |
| `fragmentLength`, `fragmentGap` | Общая фрагментация линий |
| `palette` | Общая цветовая палитра |

Масштаб рассчитывается по логическим размерам и фактическим bounds объектов. Если объекты
выходят за логическую область, используется больший размер, чтобы схема не обрезалась.

### Узлы

Узел содержит:

```text
id, title, x, y, kind, state
labelPlacement, labelOffsetX, labelOffsetY
isLoader, isTarget, menuKind, isVisible
style, bindings
```

Поддерживаемые `kind`: `Default`, `Switch`, `Mixer`, `Conveyor`, `Sensor`,
`ServicePoint`.

`menuKind`:

| Значение | Поведение |
|---|---|
| `None` | Контекстные команды отсутствуют |
| `SendOnly` | Можно назначить узел как target |
| `SendAndReturn` | Можно назначить target или loader |

В один момент времени допускается не более одного loader и одного target. На одном узле
эти роли взаимоисключающие.

### Линии

Линия ссылается на `fromNodeId` и `toNodeId`. Поддерживаются:

| kind | Назначение |
|---|---|
| `Straight` | Прямой сегмент |
| `RoundedElbow90` | Скругленный поворот 90 градусов |

Для поворота задаются `arcRadius` и `elbowOrder`. Визуальные свойства включают обычный и
активный цвет, толщину, endpoint gap, line cap, подпись и локальные параметры
фрагментации.

### Цепочки

`chains` задают упорядоченные `nodeIds` и `segmentIds`. Цепочка используется для
компоновки маршрута, вычисления bounds и привязки карточки оборудования.

### Карточки оборудования

Карточка содержит состояние, текст, bindings команд, доступность `ПУСК`/`СТОП`,
`startButtonKind`, `stopButtonKind`, стиль и привязку к цепочке. Вертикальный якорь
может быть центром bounds цепочки или конкретным узлом.

### Заглушки

`placeholderRules` автоматически резервируют место над и/или под карточкой. Доступны
`Above`, `Below`, `Both`, высота по карточке или фиксированная высота, gap и ограничение
количества.

## 6. Редактор RouteMap

Кнопка `НАСТРОЙКИ` находится в TopBar справа от `АВАРИЯ`. Она открывает
`RouteMapSettingsDialog`.

Вкладки редактора:

| Вкладка | Содержимое |
|---|---|
| Источник данных | Mock/Modbus, текущий источник и горячее переключение |
| Карта и маршруты | Размеры, padding, палитра, цепочки |
| TopBar | Тексты, цвета и bindings трех кнопок |
| Узлы | Геометрия, роли, меню, стиль, bindings |
| Линии | Endpoints, геометрия, стиль, bindings |
| Карточки | Данные, команды, типы `ПУСК`/`СТОП`, размеры и привязка |
| Заглушки | Правила автоматической компоновки |

Списки поддерживают поиск, добавление, дублирование и удаление. При переименовании ID
структурные ссылки обновляются автоматически, но `SignalId` не меняется. Удаление
блокируется, если на объект еще существуют ссылки.

Команды диалога:

| Команда | Результат |
|---|---|
| `ПРИМЕНИТЬ` | Валидирует и горячо применяет черновик без записи файла |
| `СОХРАНИТЬ` | Валидирует, атомарно сохраняет и применяет |
| `ПЕРЕЗАГРУЗИТЬ` | Повторно читает активный файл |
| `ИМПОРТ` | Загружает выбранный JSON в черновик |
| `ЭКСПОРТ` | Записывает валидный черновик в выбранный JSON |
| `ЗАКРЫТЬ` | Отбрасывает только непримененные изменения |

После успешного применения `RouteMapDashboardViewModel` заменяет definition, пересоздает
карточки, роли узлов и layout, затем повторно применяет последний snapshot сигналов.
Перезапуск приложения не нужен.

### Миграции

Поддерживается последовательная миграция `v1 -> v2 -> v3 -> v4`:

| Шаг | Изменение |
|---|---|
| v1 -> v2 | Обновление геометрии штатной цепочки |
| v2 -> v3 | Placement подписей, endpoint gap, round cap, ActiveRoute линий |
| v3 -> v4 | TopBar и bindings активности/ролей узлов |

Пользовательские цвета, размеры и существующие SignalId известных ролей сохраняются.

### Валидация

Проверяются версия схемы, уникальность ID, ссылки, endpoints, цепочки, привязки карточек,
роли bindings, типы, направления команд, координаты, размеры, цвета, радиусы дуг и правила
заглушек. Невалидный документ нельзя применить, сохранить или экспортировать.

## 7. Signal bindings

RouteMap связывает UI с runtime через `SignalBinding`:

```text
Role + SignalId + Direction + ValueType
```

### Направления

| Direction | Значение |
|---|---|
| `Read` | Только отображение/readback |
| `Write` | Только команда |
| `ReadWrite` | Команда и последующий readback |

### Роли

| Role | Назначение |
|---|---|
| `Text` | Отображаемый текст |
| `Value` | Числовое или строковое значение |
| `Visible` | Runtime-видимость |
| `StartCommand` | Команда ПУСК |
| `StopCommand` | Команда СТОП |
| `StartOffFeedback` | Read-only отключение кнопки ПУСК карточки |
| `StopOffFeedback` | Read-only отключение кнопки СТОП карточки |
| `Fault` | Признак аварии объекта |
| `ActiveRoute` | Активность узла или линии |
| `TargetCommand` | Назначение target |
| `LoaderCommand` | Назначение loader |
| `AutomaticModeCommand` | Автоматический режим |
| `ManualModeCommand` | Ручной режим |
| `ResetCommand` | Сброс |
| `EmergencyCommand` | Аварийная команда/состояние |

Стандартные SignalId:

```text
system.mode.automatic
system.mode.manual
system.reset
system.emergency
connection.status
connection.connected
system.fault
route.node.<nodeId>.active
route.node.<nodeId>.target
route.node.<nodeId>.loader
route.<segmentId>.active
equip.<equipmentId>.start
equip.<equipmentId>.stop
equip.<equipmentId>.start.off
equip.<equipmentId>.stop.off
equip.<equipmentId>.text
```

`SignalId` является доменным именем. Оно не должно содержать физический адрес PLC.
`connection.status` и `connection.connected` создает provider и в `Modbus.DataMap` не
добавляются; `system.fault` добавляется как обычная read/bool точка PLC и переводит всю
карту в общий fault-вид при `true`.

### Runtime-значение

`SignalValue` содержит:

```text
SignalId
Value
ValueType
Timestamp
IsQualityGood
IsStale
```

Dashboard получает целый словарь сигналов, преобразует его через
`RouteMapRuntimeMapper` и обновляет Avalonia UI через `Dispatcher.UIThread`. Modbus-поток
не обращается к UI напрямую.

## 8. Конфигурация Modbus TCP

В `Configurator.Boot/appsettings.json` лежат defaults, а изменяемые значения сохраняются
в `%LOCALAPPDATA%\Configurator\appsettings.json`. Используются две рабочие секции:

- `ModbusDemo` задает endpoint, lifecycle и карту данных для экрана `Modbus Demo`;
- `Modbus` хранит RouteMap `DataMap`, `AlarmMap` и `WriteConfirmationTimeoutMs`.

```json
"Modbus": {
  "WriteConfirmationTimeoutMs": 2000,
  "DataMap": [],
  "AlarmMap": []
},
"ModbusDemo": {
  "AutostartOnWorkspaceOpen": false,
  "StartupMode": "None",
  "Client": {},
  "Server": {},
  "DataMap": []
}
```

### Режим запуска

| StartupMode | Поведение |
|---|---|
| `None` | Автозапуск отключен |
| `Client` | Подключение к внешнему Modbus TCP серверу |
| `Server` | Локальный Modbus TCP сервер |
| `Both` | Одновременный запуск разрешенных ролей |

`ModbusDemo.AutostartOnWorkspaceOpen=true` запускает выбранный режим при создании Workspace.
Ошибки автозапуска логируются и не завершают приложение аварийно. При закрытии приложения
Modbus runtime останавливается.

### Endpoint

Основные поля `ModbusDemo.Client`/`ModbusDemo.Server`:

| Поле | Назначение |
|---|---|
| `Host` | Адрес внешнего сервера для Client |
| `BindAddress` | Интерфейс локального Server |
| `Port` | TCP-порт |
| `UnitId` | Modbus Unit Identifier |
| `PollIntervalMs` | Период опроса |
| `Enabled` | Разрешение роли в режиме Both |
| `CoilsEnabled` | Разрешение Coils |
| `HoldingRegistersEnabled` | Разрешение Holding Registers |
| `CoilStartAddress` | Базовый адрес Coils |
| `HoldingRegisterStartAddress` | Базовый адрес Holding Registers |
| `CoilCount` | Размер читаемой области Coils |
| `RegisterCount` | Размер читаемой области Holding Registers |

### Правило адресации

`Modbus.DataMap[].Address` — нулевое смещение внутри области endpoint:

```text
physical coil address = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

Не записывайте в `Address` документационное обозначение вида `40001`. Используйте
фактическое zero-based смещение, ожидаемое PLC и Modbus-библиотекой. Базы адресов
берутся из `ModbusDemo.Client` и `ModbusDemo.Server`.

## 9. Modbus DataMap

Каждая точка `Modbus.DataMap` имеет поля:

| Поле | Назначение |
|---|---|
| `Name` | Должно совпадать с RouteMap `SignalId` |
| `Area` | `Coil` или `HoldingRegister` |
| `Address` | Смещение относительно start address |
| `Length` | Количество coils/registers |
| `Access` | `Read`, `Write`, `ReadWrite` |
| `Type` | Логический тип Modbus |
| `BitIndex` | Бит `0..15` для Bool в Holding Register |
| `WriteMode` | `Latched` или `Pulse` |
| `PulseDurationMs` | Длительность импульса |

## 9.1 Modbus AlarmMap

`Modbus.AlarmMap` хранит операторские аварии и предупреждения, которые не являются
RouteMap `SignalId`. Записи редактируются в admin-вкладке `Менеджер тревог`; в
`Application.WorkMode=user` вкладка скрыта, но `ModbusAlarmMonitor` запускается в обоих
режимах Workspace, читает сохраненную карту и показывает диалоги по активному runtime
snapshot.

Каждая строка таблицы соответствует одному `ModbusAlarmOptions`:

| Колонка UI | Поле JSON | Назначение и допустимые значения |
|---|---|---|
| `Вкл.` | `Enabled` | `true` включает тревогу в мониторе; `false` оставляет строку в конфигурации, но диалог не появляется и acknowledgement не пишется |
| `Id` | `Id` | Непустой уникальный идентификатор; нужен для диагностики, сохранения состояния фронта и расчета повторного показа |
| `Тип` | `Kind` | `Fault` — аварийный красный диалог `Авария`; `Confirmation` — предупреждающий диалог `Повторное подтверждение`; `Message` — нейтральный диалог `Сообщение` |
| `Сообщение` | `Message` | Текст в модальном диалоге; должен быть непустым |
| `Alarm area` | `Alarm.Area` | Область входного бита: `Coil` или `HoldingRegister` |
| `Alarm Offset` | `Alarm.Address` | Zero-based offset внутри выбранной области; не является notation `40001` |
| `Alarm Bit` | `Alarm.BitIndex` | Обязателен для `HoldingRegister` и должен быть `0..15`; для `Coil` не задается |
| `Alarm client` | вычисляемое поле | Физический адрес alarm-бита по базам `ModbusDemo.Client`; ввод значения пересчитывает `Alarm.Area` и `Alarm.Address` |
| `Alarm server` | вычисляемое поле | Физический адрес alarm-бита по базам `ModbusDemo.Server`; нужен для проверки PLC-карты в другой роли |
| `OK area` | `Acknowledgement.Area` | Область отдельного acknowledgement-бита, куда монитор пишет по кнопке `Хорошо` |
| `OK Offset` | `Acknowledgement.Address` | Zero-based offset acknowledgement-бита |
| `OK Bit` | `Acknowledgement.BitIndex` | Обязателен для `HoldingRegister` и должен быть `0..15`; для `Coil` не задается |
| `OK client` | вычисляемое поле | Физический client-адрес acknowledgement-бита |
| `OK server` | вычисляемое поле | Физический server-адрес acknowledgement-бита |
| `Repeat ms` | `RepeatIntervalMs` | Интервал повторного показа, пока `Alarm=true`; валидный диапазон `1000..86400000` мс |
| `Pulse ms` | `AcknowledgementPulseDurationMs` | Длительность acknowledgement-импульса `true/false`; валидный диапазон `1..60000` мс |
| `Действие` | — | `Копия` создает дубль с новым `Id`; `Удалить` убирает строку из локального черновика |

Кнопка `ДОБАВИТЬ` создает включенную строку с `Kind=Fault`, сообщением по умолчанию,
`Alarm=Coil[0]`, `Acknowledgement=Coil[1]`, `RepeatIntervalMs=60000` и
`AcknowledgementPulseDurationMs=300`. `СОХРАНИТЬ` записывает только `Modbus.AlarmMap`;
`Modbus.DataMap` и `ModbusDemo.DataMap` не меняются. `ПЕРЕЗАГРУЗИТЬ` отбрасывает
локальный черновик и перечитывает сохраненное значение.

Адреса `Alarm` и `Acknowledgement` обязаны различаться и попадать в диапазоны
`ModbusDemo.Client`/`ModbusDemo.Server`: для `Coil` проверяются `CoilsEnabled` и
`CoilCount`, для `HoldingRegister` — `HoldingRegistersEnabled`, `RegisterCount` и
`BitIndex`. Физический адрес в колонках `Alarm client/server` и `OK client/server`
вычисляется так же, как во вкладке `SignalId ↔ Modbus`:

```text
physical coil address     = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

`AlarmMap` сохраняется в той же секции `Modbus`, что и `DataMap`, но не передается в
RouteMap facade и не появляется во вкладке `SignalId ↔ Modbus`.

В admin и user режимах монитор показывает диалог только на фронте `Alarm=true`. Если оператор нажал
`Хорошо`, acknowledgement-бит получает импульс `true`, затем `false` через `Pulse ms`.
Закрытие через `X` не пишет acknowledgement. Если alarm-бит остается `true`, тот же
диалог повторится через `Repeat ms`; когда alarm-бит станет `false`, состояние строки
сбрасывается и следующий фронт снова покажет диалог сразу.

### Coil

```json
{
  "Name": "system.emergency",
  "Area": "Coil",
  "Address": 0,
  "Length": 1,
  "Access": "ReadWrite",
  "Type": "Bool",
  "WriteMode": "Latched"
}
```

### Bool в Holding Register

```json
{
  "Name": "equip.bucket.start",
  "Area": "HoldingRegister",
  "Address": 3,
  "Length": 1,
  "Access": "ReadWrite",
  "Type": "Bool",
  "BitIndex": 0,
  "WriteMode": "Pulse",
  "PulseDurationMs": 300
}
```

Для register-bit обязательны `Type=Bool`, `Length=1`, `BitIndex=0..15`.

### Числовое значение

```json
{
  "Name": "equip.bucket.text",
  "Area": "HoldingRegister",
  "Address": 4,
  "Length": 1,
  "Access": "Read",
  "Type": "UInt16"
}
```

### Поддерживаемые типы RouteMap

| ModbusValueType | SignalValueType |
|---|---|
| `Bool` | `Bool` |
| `UInt16` | `UInt16` |
| `Int` | `Int32` или binding `Int16` |
| `Real` | `Float32` |
| `String` | `String` |

Modbus facade также знает `Date` и `Dword`, но текущий
`ModbusTcpSignalValueProvider` не публикует их в RouteMap. Для использования потребуется
расширить `SignalValueType`, mapper и проверку совместимости.

## 10. Чтение, snapshots и качество

`IModbusTcpService.Subscribe` уведомляет подписчика только при изменении конкретной точки.
Этот контракт сохранен для обратной совместимости.

`IModbusDataSnapshotSource` публикует `ModbusDataSnapshot` на каждом poll, даже если
значения не изменились. Snapshot содержит согласованный словарь именованных точек,
timestamp и состояние Modbus.

`ModbusTcpSignalValueProvider`:

- публикует только readable-точки;
- сопоставляет Modbus типы с `SignalValueType`;
- помечает значение плохим, если ни Client, ни Server не находятся в `Running`;
- помечает значение stale, если нет точки в snapshot или snapshot старше порога;
- периодически пересчитывает stale даже при отсутствии новых событий;
- добавляет синтетические системные сигналы `connection.status` и `connection.connected`.

`connection.connected=false` означает, что runtime не running или snapshot stale. В этом
состоянии RouteMap блокирует команды и рисует все узлы/линии offline-цветом, кроме
визуального выделения текущих `IsTarget`/`IsLoader` ролей. Кнопка `Н` карточки остаётся
доступной в обоих режимах: editable-значение сохраняется локально без DataMap и будет
один раз автоматически отправлено после первого Modbus reconnect.

`StaleAfterMs` ограничивается диапазоном `250..60000 ms`. Практически порог должен быть
больше `PollIntervalMs` с запасом на задержки сети и планировщика.

## 11. Запись команд

Перед записью `ModbusTcpCommandDispatcher` проверяет:

- наличие `SignalId` в `Modbus.DataMap`;
- право на запись;
- совместимость `SignalValueType` и `ModbusValueType`;
- корректность значения и карты на уровне `IModbusTcpService`.

В актуальной RouteMap schema v10 `ПУСК`, `СТОП` и `АВАРИЯ` всегда работают как
toggle-кнопки. `ПУСК` и `СТОП` взаимоисключаются: включение `ПУСК` сначала пишет
`StopCommand=false`, затем `StartCommand=true`; включение `СТОП` сначала пишет
`StartCommand=false`, затем `StopCommand=true`; ручное снятие пишет только свою команду
`false`. `StartOffFeedback`/`StopOffFeedback` являются read-only сигналами отключения:
`true` отключает соответствующую кнопку и показывает ее снятой без write-back в PLC.
Legacy-значение `RouteCommandButtonKind.Momentary` миграция приводит к `Toggle`.

`ModbusWriteMode` управляет физической записью. `Latched` хранит переданное значение,
а `Pulse` можно выбрать вручную для точек, где физически нужен импульс, но RouteMap
больше не создает pulse mapping автоматически по типу кнопки.

### Latched

`Latched` записывает переданное значение. Для readable-точки сервис ожидает readback до
`WriteConfirmationTimeoutMs`. Если подтверждение не пришло, возвращается ошибка
`ModbusWriteConfirmationTimeout`.

Используйте `Latched` для режимов, selector-команд карточки, аварийных флагов, ролей
маршрута и toggle-состояний, если PLC ожидает удерживаемое значение.

### Pulse

`Pulse` обрабатывает только запрос `true`:

1. записывает `true`;
2. ждет `PulseDurationMs`;
3. в `finally` пытается записать `false` с отдельным reset timeout 2 секунды.

Запрос `false` игнорируется. Используйте `Pulse` для TopBar `ResetCommand`; не
используйте его для режима, selector-команд карточки или состояния, которое UI должен
удерживать. Автоматические повторы неидемпотентных команд не выполняются.

### Register-bit read-modify-write

Для Bool внутри Holding Register сервис использует последнее сырое слово из snapshot как
shadow, изменяет только нужный бит и записывает целое слово. Все записи сериализованы
общим gate, поэтому параллельные изменения соседних битов не теряются.

До получения первого raw-слова запись register-bit отклоняется с
`ModbusRegisterShadowUnavailable`. После успешной записи shadow обновляется записанным
словом.

## 12. TopBar, роли и карточки

### TopBar

Стандартные bindings:

```text
AutomaticModeCommand -> system.mode.automatic
ManualModeCommand    -> system.mode.manual
ResetCommand         -> system.reset
EmergencyCommand     -> system.emergency
```

Кнопки используют оптимистическое checked-состояние и затем отправляют команду. Входной
`ReadWrite` сигнал является readback и не инициирует повторную запись.

### Loader и target

Для узла используются:

```text
TargetCommand -> route.node.<nodeId>.target
LoaderCommand -> route.node.<nodeId>.loader
```

При смене роли RouteMap записывает полный переход: сбрасывает предыдущую роль, включает
новую и при необходимости сбрасывает взаимоисключающую роль выбранного узла.

### Активный маршрут

Узлы и линии имеют независимые `ActiveRoute` bindings. Активный узел рисуется внешним
контуром, а активная линия использует active color/thickness. Эта индикация не заменяет
loader/target оформление.

У длинных линий дополнительно есть `ActiveRouteFragment` bindings:
`route.<segmentId>.fragment_<n>.active`. Они всегда `Direction=Read`,
`ValueType=Bool` и могут быть замаплены на отдельные coils или на разные bits одного
holding register. `Fault` линии остается общим для всей линии и перекрывает подсветку
отрезков. Line-level `ActiveRoute=true` подсвечивает всю линию; fragment-сигнал
подсвечивает только свой range.

### Карточка оборудования

Обычно карточка содержит `Text`, `StartCommand`, `StopCommand`, опциональные
`StartOffFeedback`/`StopOffFeedback` и runtime видимость. Команды ПУСК/СТОП должны
иметь отдельные SignalId, даже если PLC упаковывает их в разные биты одного регистра.
OffFeedback тоже настраивается отдельными read/bool SignalId и не заменяет command-bit.
Кнопка `С` использует обязательные `UncheckedCommand`/`CheckedCommand` как
`ReadWrite/Bool/Latched`; `Pulse` недопустим. TopBar `ResetCommand` использует
`ReadWrite/Bool/Pulse`: UI пишет только `true`, а Modbus dispatcher сбрасывает бит по
`PulseDurationMs`. При хорошем карточном `Enabled=false` кнопка `Н` не зависит от
регистра: в `user` она доступна только после подключения, в `admin` — всегда. Кнопка
`С` остается доступной при наличии связи и один раз пишет
`CheckedCommand=false`, затем `UncheckedCommand=false`.

## 13. Диагностика

При `SignalSource=Modbus` выполняется автоматическая проверка каждого binding:

- SignalId отсутствует в `Modbus.DataMap`;
- binding требует чтение, но точка write-only;
- binding требует запись, но точка read-only;
- тип RouteMap не совпадает с типом Modbus.

Проблемы пишутся как warning и не завершают приложение. Проверка повторяется при горячем
изменении RouteMap definition или Modbus options. Проверка включает `ActiveRouteFragment`
bindings, поэтому отсутствующий `route.<segmentId>.fragment_<n>.active` будет виден в
warning-логах так же, как обычный `ActiveRoute`.

Основные причины отсутствия данных:

| Симптом | Проверка |
|---|---|
| UI работает только в Mock | `RouteMapRuntime.SignalSource` |
| User сбрасывает admin-настройки | Общий `%LOCALAPPDATA%\Configurator\appsettings.json` и overlay runtime config |
| Все значения offline/stale | Состояние Client/Server и poll interval |
| Один сигнал отсутствует | Точное совпадение `Name == SignalId` |
| Неверное значение бита | `Address`, `BitIndex`, word layout PLC |
| Команда read-only | `Access` точки |
| Команда не подтверждается | PLC readback и `WriteConfirmationTimeoutMs` |
| Bit-write отклонен | Получен ли первый snapshot регистра |
| Pulse не подходит | Изменить `WriteMode` на `Latched` |
| Карта не загружается | `LastLoadError`, schemaVersion, JSON и валидация |

## 14. Рекомендуемый ввод в эксплуатацию

1. Проверить карту и редактор в `Mock`.
2. Зафиксировать список всех SignalId из RouteMap.
3. Получить официальную PLC-карту адресов и bit layout.
4. Заполнить production `Modbus.DataMap` без изменения UI.
5. Включить `Modbus` и только read-only сигналы.
6. Проверить connection, stale, reconnect и восстановление.
7. Проверить активность линий и узлов.
8. Проверить режимы, `system.fault` и аварийный readback.
9. Проверить loader/target и взаимоисключение ролей.
10. По одной разрешить команды ПУСК/СТОП и проверить их взаимоисключение с OffFeedback.
11. Проверить timeout, отмену и потерю связи во время команды.
12. Только после стендовых проверок переносить production-адреса.

## 15. Добавление нового сигнала

### Read-only Bool

1. Добавьте binding в редактор RouteMap.
2. Назначьте уникальный `SignalId`, `Direction=Read`, `ValueType=Bool`.
3. Добавьте точку с тем же `Name` в `Modbus.DataMap`.
4. Укажите `Access=Read` и физическую область.
5. Перезапустите приложение, если изменялся default/shared `appsettings.json`.
6. Проверьте warning-логи и значение в UI.

Для отрезка линии используйте секцию `Линии` → `Отрезки`; роль фиксирована как
`ActiveRouteFragment`, а имя по умолчанию имеет вид
`route.<segmentId>.fragment_<n>.active`.

### Команда

1. Выберите подходящую командную роль.
2. Используйте `Direction=ReadWrite`, если PLC дает readback.
3. Добавьте writable-точку в `DataMap`.
4. Выберите `Latched` или `Pulse` по PLC-контракту.
5. Для register-bit дождитесь первого snapshot до записи.
6. Проверьте положительный сценарий, timeout и потерю связи.

## 16. Тестирование

Полная проверка solution:

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

Отдельные наборы:

```powershell
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.OpcUa.Tests\Configurator.Infrastructure.OpcUa.Tests.csproj --no-restore
```

Текущий набор покрывает RouteMap mapping/configuration/layout, headless Avalonia UI,
Modbus validation, register-bit decode/write, shadow, конкурентные записи, snapshots,
Latched/Pulse, quality/stale, reconnect и локальный Modbus TCP server.

## 17. Ограничения и правила сопровождения

- Не добавляйте Modbus-адреса в XAML, RouteMap ViewModel или `route-map.json`.
- Не смешивайте RouteMap-карту `Modbus.DataMap` с demo-картой `ModbusDemo.DataMap`.
- Не добавляйте тревоги в `Modbus.DataMap`: используйте `Modbus.AlarmMap`; исключение не требуется для `system.fault`, потому что это не диалог тревоги, а read/bool SignalId общей аварии карты.
- Не обновляйте Avalonia controls из Modbus callback.
- Не меняйте `SignalId` при изменении только физического адреса PLC.
- Не назначайте Pulse без подтвержденной семантики PLC.
- Не выполняйте автоматический retry команд с побочными эффектами.
- Не регистрируйте `RouteMapDefinition` как неизменяемый singleton: актуальная definition
  принадлежит `RouteMapConfigurationManager`.
- Не переносите interlock и безопасность из PLC в UI.

## 18. Связанные документы

- `Configurator.Desktop/signal_id_modbus_tcp_mapping_guide.md` — SignalId и настройка физических связей.
- `Configurator.Desktop/route_map_programmer_guide.md` — детали RouteMap UI, геометрии и редактора.
- `Configurator.Desktop/modbus_tcp_integration_guide.md` — краткий гайд подключения Modbus.
- `Configurator.Desktop/Описание ModbusDemo.md` — экран Modbus Demo и общий TCP runtime.
- `docs/promflow_dispatcher_route_map_ui_merge_recommendations.md` — итоговые рекомендации по слиянию.
