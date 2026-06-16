# RouteMap и Modbus TCP: полное описание и руководство

## 1. Назначение

Документ описывает фактическую реализацию RouteMap и ее интеграцию с Modbus TCP в
`PromFlow.Dispatcher`, ветка `3-route-map-merge`.

RouteMap является первой вкладкой Workspace и экраном по умолчанию. Она отображает
технологический маршрут, состояние узлов и линий, роли погрузки/назначения, карточки
оборудования, TopBar и команды оператора. Источником данных может быть встроенный Mock
или основной Modbus TCP runtime.

Диагностические экраны `Modbus TCP`, `Modbus Demo` и `OPC UA` остаются отдельными
вкладками. `ModbusDemo` имеет собственную секцию конфигурации и не используется как
источник RouteMap.

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

1. Заполните секции `Modbus.Client`, `Modbus.Server` и `Modbus.DataMap`.
2. Убедитесь, что `DataMap[].Name` совпадает с `SignalId` RouteMap.
3. Переключите `RouteMapRuntime.SignalSource` в `Modbus`.
4. Настройте `Modbus.StartupMode` и `AutostartOnWorkspaceOpen`.
5. Сначала проверьте read-only сигналы, затем разрешайте команды.

Пример:

```json
"RouteMapRuntime": {
  "SignalSource": "Modbus",
  "StaleAfterMs": 1500
},
"Modbus": {
  "AutostartOnWorkspaceOpen": true,
  "StartupMode": "Client"
}
```

## 3. Архитектура

### Чтение данных

```text
ModbusClientService / ModbusServerService
  -> ModbusRuntimeService
  -> ModbusTcpService
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
  -> IModbusTcpService.SetAsync
  -> Modbus client или локальный server runtime
  -> обычный poll/readback
```

RouteMap не знает IP-адресов, UnitId, номеров регистров и coils. UI работает только с
доменными `SignalId`, типами и направлениями bindings. Физическая адресация находится в
`Modbus.DataMap`.

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
| Основной Modbus facade | `Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs` |
| DI и выбор источника | `Configurator.Boot/Program.cs` |
| Runtime-конфигурация | `Configurator.Boot/appsettings.json` |
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

Текущая версия схемы — `4`:

```json
{
  "schemaVersion": 4,
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

Карточка содержит состояние, текст, bindings команд, доступность `ПУСК`/`СТОП`, стиль и
привязку к цепочке. Вертикальный якорь может быть центром bounds цепочки или конкретным
узлом.

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
| Карточки | Данные, команды, размеры и привязка |
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
| `State` | Состояние объекта |
| `Text` | Отображаемый текст |
| `Value` | Числовое или строковое значение |
| `Visible` | Runtime-видимость |
| `StartCommand` | Команда ПУСК |
| `StopCommand` | Команда СТОП |
| `Fault` | Признак аварии объекта |
| `ActiveRoute` | Активность узла или линии |
| `TargetCommand` | Назначение target |
| `LoaderCommand` | Назначение loader |
| `AutomaticModeCommand` | Автоматический режим |
| `ManualModeCommand` | Ручной режим |
| `EmergencyCommand` | Аварийная команда/состояние |

Стандартные SignalId:

```text
system.mode.automatic
system.mode.manual
system.emergency
connection.status
route.node.<nodeId>.active
route.node.<nodeId>.target
route.node.<nodeId>.loader
route.<segmentId>.active
equip.<equipmentId>.start
equip.<equipmentId>.stop
equip.<equipmentId>.text
```

`SignalId` является доменным именем. Оно не должно содержать физический адрес PLC.

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

Основная секция находится в `Configurator.Boot/appsettings.json`:

```json
"Modbus": {
  "AutostartOnWorkspaceOpen": false,
  "StartupMode": "None",
  "WriteConfirmationTimeoutMs": 2000,
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

`AutostartOnWorkspaceOpen=true` запускает выбранный режим при создании Workspace.
Ошибки автозапуска логируются и не завершают приложение аварийно. При закрытии приложения
Modbus runtime останавливается.

### Endpoint

Основные поля клиента/сервера:

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

`DataMap[].Address` — нулевое смещение внутри области endpoint:

```text
physical coil address = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

Не записывайте в `Address` документационное обозначение вида `40001`. Используйте
фактическое zero-based смещение, ожидаемое PLC и Modbus-библиотекой.

## 9. Modbus DataMap

Каждая точка имеет поля:

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
- добавляет синтетический строковый сигнал `connection.status`.

`StaleAfterMs` ограничивается диапазоном `250..60000 ms`. Практически порог должен быть
больше `PollIntervalMs` с запасом на задержки сети и планировщика.

## 11. Запись команд

Перед записью `ModbusTcpCommandDispatcher` проверяет:

- наличие `SignalId` в `Modbus.DataMap`;
- право на запись;
- совместимость `SignalValueType` и `ModbusValueType`;
- корректность значения и карты на уровне `IModbusTcpService`.

### Latched

`Latched` записывает переданное значение. Для readable-точки сервис ожидает readback до
`WriteConfirmationTimeoutMs`. Если подтверждение не пришло, возвращается ошибка
`ModbusWriteConfirmationTimeout`.

Используйте `Latched` для режимов, аварийных флагов, ролей маршрута и toggle-состояний,
если PLC ожидает удерживаемое значение.

### Pulse

`Pulse` обрабатывает только запрос `true`:

1. записывает `true`;
2. ждет `PulseDurationMs`;
3. в `finally` пытается записать `false` с отдельным reset timeout 2 секунды.

Запрос `false` игнорируется. Не используйте `Pulse` для режима или состояния, которое UI
должен удерживать. Автоматические повторы неидемпотентных команд не выполняются.

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

### Карточка оборудования

Обычно карточка содержит `State`, `Text`, `StartCommand`, `StopCommand` и runtime
видимость. Команды ПУСК/СТОП должны иметь отдельные SignalId, даже если PLC упаковывает
их в разные биты одного регистра.

## 13. Диагностика

При `SignalSource=Modbus` выполняется автоматическая проверка каждого binding:

- SignalId отсутствует в `Modbus.DataMap`;
- binding требует чтение, но точка write-only;
- binding требует запись, но точка read-only;
- тип RouteMap не совпадает с типом Modbus.

Проблемы пишутся как warning и не завершают приложение. Проверка повторяется при горячем
изменении RouteMap definition или Modbus options.

Основные причины отсутствия данных:

| Симптом | Проверка |
|---|---|
| UI работает только в Mock | `RouteMapRuntime.SignalSource` |
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
8. Проверить режимы и аварийный readback.
9. Проверить loader/target и взаимоисключение ролей.
10. По одной разрешить команды ПУСК/СТОП.
11. Проверить timeout, отмену и потерю связи во время команды.
12. Только после стендовых проверок переносить production-адреса.

## 15. Добавление нового сигнала

### Read-only Bool

1. Добавьте binding в редактор RouteMap.
2. Назначьте уникальный `SignalId`, `Direction=Read`, `ValueType=Bool`.
3. Добавьте точку с тем же `Name` в `Modbus.DataMap`.
4. Укажите `Access=Read` и физическую область.
5. Перезапустите приложение, если изменялся `appsettings.json`.
6. Проверьте warning-логи и значение в UI.

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
- Не смешивайте основную `Modbus.DataMap` с `ModbusDemo.DataMap`.
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
- `Configurator.Desktop/Описание ModbusDemo.md` — независимый диагностический ModbusDemo.
- `docs/promflow_dispatcher_route_map_ui_merge_recommendations.md` — итоговые рекомендации по слиянию.
