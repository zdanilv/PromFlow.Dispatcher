# SignalId и привязка к Modbus TCP

## 1. Что такое SignalId

`SignalId` — стабильное доменное имя сигнала, которым RouteMap связывает элемент UI с
источником данных и командами. Оно описывает смысл сигнала, а не его физическое
расположение в PLC.

Примеры:

```text
system.mode.automatic
system.mode.manual
system.emergency
route.node.bsu_1.active
route.node.bsu_1.target
route.node.bsu_1.loader
route.active_bsu1_bsu2.active
equip.bucket.start
equip.bucket.stop
equip.bucket.text
```

SignalId остается тем же при переносе сигнала на другой coil, регистр или бит. Физический
адрес меняется только в `Modbus.DataMap`.

```text
RouteMap binding: equip.bucket.start
Modbus DataMap:    Holding Register, offset 3, bit 0
```

Такое разделение не позволяет PLC-адресам проникать в XAML, ViewModel и
`route-map.json`.

## 2. Правила именования

Рекомендуемый формат:

```text
<область>.<объект>.<свойство или команда>
```

Используйте латинские буквы в нижнем регистре, цифры, точки и `_` внутри идентификатора
объекта. Имена должны описывать назначение:

```text
route.node.bsu_2.active
equip.mixer_1.start
equip.mixer_1.fault
```

Не используйте адреса и транспортные детали:

```text
coil_15                 # плохо
register_40003_bit_2    # плохо
plc1.db20.value         # плохо, если это физическое расположение
```

Сравнение SignalId в Modbus-интеграции выполняется без учета регистра, но рекомендуется
везде использовать одно написание.

## 3. Где SignalId задается в RouteMap

Откройте `Route Map` → `НАСТРОЙКИ`. Bindings редактируются на вкладках:

- `TopBar`;
- `Узлы`;
- `Линии`;
- `Карточки`.

Каждый binding содержит:

| Поле | Назначение |
|---|---|
| `Role` | Как RouteMap использует сигнал |
| `SignalId` | Доменное имя |
| `Direction` | Чтение, запись или readback |
| `ValueType` | Тип значения, ожидаемый UI |

### Направления

| Direction | Требуемый Modbus Access |
|---|---|
| `Read` | `Read` или `ReadWrite` |
| `Write` | `Write` или `ReadWrite` |
| `ReadWrite` | `ReadWrite` |

`ReadWrite` используется для команды, состояние которой PLC возвращает обычным poll.

### Типы RouteMap

| SignalValueType | Обычный Modbus Type |
|---|---|
| `Bool` | `Bool` |
| `UInt16` | `UInt16` |
| `Int16`, `Int32` | `Int` |
| `Float32` | `Real` |
| `String` | `String` |

### Основные роли

| Role | Пример SignalId |
|---|---|
| `AutomaticModeCommand` | `system.mode.automatic` |
| `ManualModeCommand` | `system.mode.manual` |
| `EmergencyCommand` | `system.emergency` |
| `ActiveRoute` | `route.node.bsu_1.active` |
| `TargetCommand` | `route.node.bsu_1.target` |
| `LoaderCommand` | `route.node.bsu_1.loader` |
| `StartCommand` | `equip.bucket.start` |
| `StopCommand` | `equip.bucket.stop` |
| `Text` | `equip.bucket.text` |

Один SignalId может использоваться несколькими объектами, если тип совместим. Новая
вкладка группирует такие usages и вычисляет наиболее строгий требуемый доступ.

### Роли узлов, линий и карточек

RouteMap меняет состояние элементов только через `SignalBinding`: роль задается в
настройках `Route Map`, а физический адрес этой роли настраивается во вкладке
`SignalId ↔ Modbus`. Для ролей чтения PLC должен записать значение по адресу из
`Modbus.DataMap`; следующий snapshot обновит UI. Для ролей команд UI сам пишет значение
в PLC по адресу выбранного `SignalId`.

Общие роли объектов:

| Роль | Где используется | Direction | ValueType | Что меняет в UI |
|---|---|---|---|---|
| `Visible` | Узлы, линии, карточки | `Read` | `Bool` | `true` показывает объект, `false` скрывает. |
| `Fault` | Узлы, линии, карточки | `Read` | `Bool` | `true` переводит объект в аварийный цвет и запрещает команды карточки. |
| `ActiveRoute` | Узлы, линии | `Read` | `Bool` | `true` показывает объект как часть активного маршрута. |
| `Text` | Карточки | `Read` | `String` или числовой тип | Меняет текст статуса карточки. |
| `Value` | Карточки и объекты runtime | `Read` | Любой поддержанный тип | Читает дополнительное значение; стандартная карточка не выводит отдельное поле значения. |

SignalId вида `*.state` больше не являются активными ролями RouteMap. Старые JSON
могут содержать `SignalBindingRole.State`, но миграция актуальной схемы удаляет такие
bindings, runtime их не читает, а вкладка `SignalId ↔ Modbus` не показывает их в
inventory. Старые точки `*.state` в `Modbus.DataMap` не удаляются автоматически, чтобы
не стереть пользовательские адреса.

Если качество любого активного сигнала объекта плохое или значение stale, объект
становится `Offline`. `Fault=true` имеет приоритет над `ActiveRoute=true`, а
`ActiveRoute=true` подсвечивает активный маршрут, если объект не аварийный, не offline
и не disabled. Если этих сигналов нет, используется статическое fallback-состояние из
настроек Route Map.

#### Узлы

В стандартном seed у узлов есть роли:

| Роль | Direction | ValueType | Пример SignalId | Пример Modbus mapping | Пример значения PLC |
|---|---|---|---|---|---|
| `Fault` | `Read` | `Bool` | `bsu_1.fault` | `Coil`, `Address=20`, `Type=Bool` | `true` переводит узел в аварию. |
| `ActiveRoute` | `Read` | `Bool` | `route.node.bsu_1.active` | `Coil`, `Address=21`, `Type=Bool` | `true` рисует активный контур узла. |
| `TargetCommand` | `ReadWrite` | `Bool` | `route.node.bsu_1.target` | `Coil`, `Address=22`, `Type=Bool` | UI пишет `true/false`; PLC readback держит пункт `Отправить` выбранным. |
| `LoaderCommand` | `ReadWrite` | `Bool` | `route.node.bsu_1.loader` | `Coil`, `Address=23`, `Type=Bool` | UI пишет `true/false`; PLC readback держит пункт `Возврат` выбранным. |

`TargetCommand` появляется у узлов с `MenuKind=SendOnly` или `SendAndReturn`.
`LoaderCommand` появляется только у `SendAndReturn`. Чтобы PLC сам изменил выбранный
пункт меню узла, он должен вернуть `true` на соответствующий command/readback адрес и
`false` на остальные конкурирующие узлы. Например, если PLC пишет
`route.node.bsu_1.target=true`, узел `БСУ 1` становится выбранной точкой `Отправить`;
если затем `route.node.bsu_2.target=true`, старый адрес `bsu_1.target` должен стать
`false`, чтобы UI не показывал два пункта отправки одновременно.

Дополнительно для узла можно добавить `Visible`:

```text
Role      = Visible
SignalId  = route.node.bsu_1.visible
Direction = Read
ValueType = Bool
DataMap   = Coil, Address=24, Type=Bool, Access=Read
PLC value = false -> узел скрыт с карты и недоступен для клика
```

#### Линии

В стандартном seed у линий есть роли:

| Роль | Direction | ValueType | Пример SignalId | Пример Modbus mapping | Пример значения PLC |
|---|---|---|---|---|---|
| `Fault` | `Read` | `Bool` | `active_bsu1_bsu2.fault` | `Coil`, `Address=31`, `Type=Bool` | `true` окрашивает линию как аварийную. |
| `ActiveRoute` | `Read` | `Bool` | `route.active_bsu1_bsu2.active` | `Coil`, `Address=32`, `Type=Bool` | `true` подсвечивает линию как участок текущего маршрута. |

Чтобы PLC выделил линию активного маршрута, настройте `ActiveRoute` на bool-адрес и
запишите туда `true`. Чтобы снять выделение, PLC должен вернуть `false`.

Дополнительно для линии можно добавить `Visible`:

```text
Role      = Visible
SignalId  = route.active_bsu1_bsu2.visible
Direction = Read
ValueType = Bool
DataMap   = Coil, Address=33, Type=Bool, Access=Read
PLC value = false -> линия скрыта
```

`Text` и `Value` runtime может прочитать для любого объекта, но стандартная карта
не выводит динамический текст поверх узлов и линий: подписи узлов и линий берутся из
RouteMap definition. Для динамической подписи потребуется отдельная доработка UI.

#### Карточки

В стандартном seed у карточки `equip.bucket` есть роли:

| Роль | Direction | ValueType | Пример SignalId | Пример Modbus mapping | Пример значения PLC/UI |
|---|---|---|---|---|---|
| `Text` | `Read` | `UInt16` | `equip.bucket.text` | `HoldingRegister`, `Address=44`, `Type=UInt16` | `0` показывает статус `Выключен`, `1` показывает `Ожидание`, `3` показывает `Выполнение`. |
| `StartCommand` | `ReadWrite` | `Bool` | `equip.bucket.start` | `Coil`, `Address=45`, `Type=Bool` | Кнопка `ПУСК` пишет команду; PLC readback может удерживать toggle включенным. |
| `StopCommand` | `ReadWrite` | `Bool` | `equip.bucket.stop` | `Coil`, `Address=47`, `Type=Bool` | Кнопка `СТОП` пишет команду; PLC readback может удерживать toggle включенным. |

Коды для `Text` карточки:

| Код PLC | Статус карточки |
|---|---|
| `0` | `Выключен`, legacy-синоним, серый `MutedTextBrush` |
| `1` | `Ожидание`, желтый `WarningBrush` |
| `2` | `Авария` |
| `3` | `Выполнение` |
| `4` | `Выгрузка` |
| `5` | `Загрузка` |

Можно передать и строку, если binding `Text` настроен как `SignalValueType.String` и
точка `Modbus.DataMap` имеет `Type=String`.

Дополнительные роли карточки:

```text
Role      = Visible
SignalId  = equip.bucket.visible
Direction = Read
ValueType = Bool
DataMap   = Coil, Address=49, Type=Bool, Access=Read
PLC value = false -> карточка скрыта
```

```text
Role      = Fault
SignalId  = equip.bucket.fault
Direction = Read
ValueType = Bool
DataMap   = Coil, Address=50, Type=Bool, Access=Read
PLC value = true -> карточка аварийная, ПУСК/СТОП недоступны
```

```text
Role      = Value
SignalId  = equip.bucket.weight
Direction = Read
ValueType = UInt16
DataMap   = HoldingRegister, Address=51, Type=UInt16, Access=Read
PLC value = 1250 -> значение попадет в runtime как ValueText; стандартная карточка его отдельно не показывает
```

`ПУСК` и `СТОП` всегда работают как обычные `ToggleButton`: при включении UI пишет
`true` в `StartCommand`/`StopCommand`, при снятии галочки пишет `false` в тот же
command-binding. Отдельных ролей OffFeedback и режима `Momentary` для этих кнопок
в актуальной схеме RouteMap нет.

## 4. Вкладка SignalId ↔ Modbus

В Workspace вкладки расположены так:

```text
Route Map
SignalId ↔ Modbus
Modbus Demo
```

Вкладка автоматически читает актуальную RouteMap definition и показывает все уникальные
SignalId из TopBar, узлов, линий, vehicles и карточек.

Сигналы разделены на сворачиваемые группы в фиксированном порядке:
`Системные`, `TopBar`, `Узлы`, `Линии`, `Карточки`, `Объекты`, `Общие`. Пустые группы
не отображаются, остальные раскрыты по умолчанию. Если один SignalId используется
элементами разных категорий, он показывается одной редактируемой строкой в группе
`Общие`; все роли и объекты остаются перечислены в строке. Состояние раскрытия групп
сохраняется при горячем обновлении definition и Modbus options в текущей сессии, но
не записывается в конфигурационные файлы.

Для каждой строки отображаются:

- роли и объекты RouteMap;
- ожидаемый `SignalValueType`;
- требуемый доступ;
- состояние привязки;
- `Area`, `Address`, `Length`, `BitIndex`;
- фактический `Access` и Modbus `Type`;
- `WriteMode`, `PulseDurationMs`;
- редактируемые физические адреса Client и Server.

Состояния:

| Состояние | Значение |
|---|---|
| `Настроен` | В `Modbus.DataMap` есть совместимая точка |
| `Не настроен` | SignalId присутствует в RouteMap, но точки нет |
| `Ошибка` | Тип, доступ или параметры точки несовместимы |
| `Системный` | Сигнал создается runtime и не требует PLC-адреса |

`connection.status` является системным сигналом. Его создает
`ModbusTcpSignalValueProvider`; добавлять его в `DataMap` не нужно.

### Создание связи

1. Найдите строку `Не настроен`.
2. Нажмите `Создать`.
3. Выберите область `Area` и задайте `Offset` или физический адрес Client/Server.
4. Для Bool в регистре выберите `HoldingRegister` и укажите `BitIndex`.
5. Для `Coil` поле `Bit` очищается автоматически и не сохраняется.
6. Проверьте Access, WriteMode и физические адреса.
7. Нажмите `СОХРАНИТЬ`.

Автоматические значения по умолчанию:

| SignalValueType | Area | Type | Length |
|---|---|---|---|
| `Bool` | `Coil` | `Bool` | 1 |
| `UInt16` | `HoldingRegister` | `UInt16` | 1 |
| `Int16`, `Int32` | `HoldingRegister` | `Int` | 1 |
| `Float32` | `HoldingRegister` | `Real` | 2 |
| `String` | `HoldingRegister` | `String` | 1 |

Для новых связей RouteMap `WriteMode` по умолчанию становится `Latched`.
Если физически нужна импульсная запись, выберите `Pulse` вручную для нужной
точки `Modbus.DataMap`; UI-кнопки `ПУСК`, `СТОП` и `АВАРИЯ` больше не переводят
mapping в pulse-режим автоматически.

Адрес всегда вводится пользователем по официальной карте PLC. Можно вводить как `Offset`,
так и физический адрес в колонках Client/Server. Вкладка не пытается самостоятельно
распределять production-адреса.

Если Bool-сигнал узла, линии или карточки должен лежать в бите Holding Register,
можно сразу ввести физический register address в колонке Client/Server. Вкладка сверит
адрес с диапазонами `ModbusDemo.Client/Server`, переключит `Area` на `HoldingRegister`,
пересчитает `Offset` и разблокирует поле `Bit`. Например при
`HoldingRegisterStartAddress = 16384` ввод `16420` даст `Offset = 36`; после этого
можно указать `BitIndex`, например `2`.

### Удаление связи

Кнопка `Удалить` удаляет из RouteMap-карты `Modbus.DataMap` только точку выбранного SignalId
после сохранения. Сторонние и диагностические точки, которых нет в актуальной RouteMap,
сохраняются без изменений.

### Изменение RouteMap и Modbus снаружи

После горячего применения новой RouteMap definition список SignalId перестраивается.
Несохраненные строки с теми же SignalId сохраняют введенные значения.

Если `Modbus.DataMap` изменен снаружи или непосредственно в конфигурации:

- при чистом редакторе список обновляется автоматически;
- при локальном черновике показывается предупреждение;
- `ПЕРЕЗАГРУЗИТЬ` отбрасывает черновик и принимает внешние изменения.

## 5. От offset до физического адреса

`DataMap.Address` — zero-based offset относительно endpoint. В UI можно редактировать
физический адрес Client или Server; вкладка пересчитает его обратно в тот же offset:

```text
physical coil address = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

Пример:

```text
HoldingRegisterStartAddress = 100
Address                     = 3
BitIndex                    = 5

Физический адрес: register 103, bit 5
```

Если в колонке Client address ввести `103`, при базе `HoldingRegisterStartAddress = 100`
в `Modbus.DataMap` сохранится `Address = 3`. Если затем база Server равна `300`, колонка
Server address покажет `303`.

Не вводите обозначение `40001` только потому, что оно напечатано в документации PLC.
Сначала уточните, использует ли документация 0-based или 1-based notation, и введите
фактический адрес или уже подготовленный offset.

Вкладка отдельно показывает и позволяет редактировать адрес для Client и Server. Базы
адресов берутся из `ModbusDemo.Client` и `ModbusDemo.Server`, потому что именно экран
`Modbus Demo` владеет TCP endpoint и lifecycle. Сохраняется при этом только общий
`Modbus.DataMap`, поэтому разные физические адреса Client/Server пересчитываются в один
offset.

## 6. Варианты хранения Bool

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

Для Coil `Length` всегда равен 1, `BitIndex` отсутствует.

### Бит Holding Register

```json
{
  "Name": "equip.bucket.start",
  "Area": "HoldingRegister",
  "Address": 3,
  "Length": 1,
  "BitIndex": 0,
  "Access": "ReadWrite",
  "Type": "Bool",
  "WriteMode": "Pulse",
  "PulseDurationMs": 300
}
```

Правила register-bit:

- `Type=Bool`;
- `Length=1`;
- `BitIndex=0..15`;
- разные SignalId могут занимать разные биты одного слова;
- одинаковый бит нельзя назначить двум SignalId;
- целое значение не может пересекаться со словом, используемым как register-bit.

Запись выполняется как сериализованный read-modify-write. Перед первой записью сервис
должен получить raw-слово в snapshot. Иначе возвращается
`ModbusRegisterShadowUnavailable`.

## 7. Access, Latched, Pulse и readback

### Access

`Read` используется для индикации, `Write` — для команды без чтения, `ReadWrite` — для
команды с readback. Вкладка проверяет Access относительно всех usages SignalId.

### Latched

`Latched` записывает переданное значение `true/false` или число. Для readable-точки
сервис ожидает подтверждение обычным poll до `WriteConfirmationTimeoutMs`.

Используйте `Latched` для:

- режимов;
- ролей loader/target;
- toggle-состояний;
- значений, которые PLC должен удерживать.

### Pulse

`Pulse` принимает только запрос `true`, записывает `true`, ждет `PulseDurationMs`, затем
обязательно пытается записать `false`.

Используйте его только когда PLC ожидает фронт или короткий импульс. Не назначайте Pulse
режимам и ролям маршрута без подтвержденного PLC-контракта.

Неидемпотентные команды автоматически не повторяются.

## 8. Переключение Mock и Modbus

Откройте `Route Map` → `НАСТРОЙКИ` → `Источник данных`.

Флаг `Использовать Mock-симуляцию`:

| Значение | Источник |
|---|---|
| Включен | `Mock` |
| Выключен | `Modbus` |

`ПРИМЕНИТЬ` переключает источник только для текущей сессии. `СОХРАНИТЬ` записывает
`RouteMapRuntime.SignalSource` в `appsettings.json` и переключает источник немедленно.

Dashboard и его подписчики не пересоздаются. Старый backend отключается, новые команды и
snapshots направляются выбранному backend.

Переключение на Modbus не запускает TCP runtime. Соединение запускается:

- вручную на вкладке `Modbus Demo`;
- автоматически через `ModbusDemo.AutostartOnWorkspaceOpen` и `StartupMode`.

Если Modbus остановлен, RouteMap получает bad quality/stale вместо аварийного завершения.

## 9. Горячее применение DataMap

После сохранения вкладка:

1. записывает секцию `Modbus`, сохраняя сторонние точки RouteMap-карты;
2. передает новый DataMap в `IModbusDataMapRuntime`;
3. атомарно заменяет lookup работающего facade без разрыва TCP-соединения;
4. очищает старые значения и register shadow;
5. получает значения новой карты на следующем poll.

Если hot apply не удался, файл остается сохраненным, а UI сообщает, что изменения
вступят в силу после перезапуска Modbus runtime.

## 10. Добавление нового сигнала: полный пример

Требуется добавить команду запуска смесителя.

1. В редакторе RouteMap откройте нужную карточку.
2. Добавьте binding:

```text
Role       = StartCommand
SignalId   = equip.mixer_1.start
Direction  = ReadWrite
ValueType  = Bool
```

3. Нажмите `ПРИМЕНИТЬ` или `СОХРАНИТЬ`.
4. Откройте `SignalId ↔ Modbus`.
5. Найдите `equip.mixer_1.start` и нажмите `Создать`.
6. По карте PLC задайте, например:

```text
Area            = HoldingRegister
Address         = 12
Length          = 1
BitIndex        = 4
Access          = ReadWrite
Type            = Bool
WriteMode       = Pulse
PulseDurationMs = 300
```

7. Проверьте вычисленный physical address.
8. Нажмите `СОХРАНИТЬ`.
9. Запустите Modbus Client.
10. Проверьте первый snapshot, команду, reset импульса и readback PLC.

## 11. Диагностика

| Проблема | Что проверить |
|---|---|
| `Не настроен` | Для SignalId отсутствует строка DataMap |
| `Ошибка` типа | `SignalValueType` и Modbus `Type` |
| `Ошибка` доступа | Direction binding и Access точки |
| Конфликт адреса | Дубли Coil, бита или пересекающиеся регистры |
| Неверный сигнал | Точное правило `DataMap.Name == SignalId` |
| UI продолжает симуляцию | Флаг Mock и текущий источник в настройках RouteMap |
| Все значения stale | Состояние Modbus и `StaleAfterMs` |
| Bit-write недоступен | Дождаться первого успешного snapshot слова |
| Нет readback | Access, PLC echo и `WriteConfirmationTimeoutMs` |
| Неверный physical address | StartAddress, 0/1-based notation и offset |
| Изменения не видны | Сообщение hot apply; при ошибке перезапустить runtime |

Дополнительная автоматическая диагностика пишет warning для отсутствующих SignalId,
неверного доступа и несовместимых типов, когда RouteMap использует Modbus.

## 12. Checklist production-карты PLC

1. Получить утвержденную карту coils/registers и bit layout.
2. Уточнить 0-based/1-based notation каждого диапазона.
3. Настроить Host, Port, UnitId, StartAddress и Count на вкладке Modbus Demo.
4. Проверить все RouteMap SignalId во вкладке сопоставлений.
5. Устранить строки `Не настроен` и `Ошибка`.
6. Проверить отсутствие физических конфликтов.
7. Сначала включить только read-only сигналы.
8. Проверить quality, stale, reconnect и восстановление.
9. Проверить активные узлы и линии.
10. Проверить режимы, emergency и роли loader/target.
11. По одной разрешить команды оборудования.
12. Проверить Latched/Pulse, timeout и потерю связи во время записи.
13. Убедиться, что interlock и безопасность реализованы в PLC.

## 13. Связанные документы

- `route_map_modbus_tcp_full_guide.md` — полное описание RouteMap и Modbus TCP.
- `route_map_programmer_guide.md` — структура RouteMap, JSON и редактор.
- `modbus_tcp_integration_guide.md` — краткое руководство интеграции.
- `Описание ModbusDemo.md` — экран Modbus Demo и общий TCP runtime.
