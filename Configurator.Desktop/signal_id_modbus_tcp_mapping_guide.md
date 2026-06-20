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
- вычисленные адреса Client и Server.

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
3. Выберите физическую область и offset.
4. Для Bool в регистре выберите `HoldingRegister` и укажите `BitIndex`.
5. Проверьте Access, WriteMode и физические адреса.
6. Нажмите `СОХРАНИТЬ`.

Автоматические значения по умолчанию:

| SignalValueType | Area | Type | Length |
|---|---|---|---|
| `Bool` | `Coil` | `Bool` | 1 |
| `UInt16` | `HoldingRegister` | `UInt16` | 1 |
| `Int16`, `Int32` | `HoldingRegister` | `Int` | 1 |
| `Float32` | `HoldingRegister` | `Real` | 2 |
| `String` | `HoldingRegister` | `String` | 1 |

Для новых связей, созданных от momentary-команд RouteMap (`ПУСК`, `СТОП`, `АВАРИЯ`),
`WriteMode` по умолчанию становится `Pulse`. Для остальных команд и для старых
существующих точек значение не меняется автоматически.

Адрес всегда вводится пользователем по официальной карте PLC. Вкладка не пытается
самостоятельно распределять production-адреса.

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

`DataMap.Address` — zero-based offset относительно endpoint:

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

Не вводите обозначение `40001` только потому, что оно напечатано в документации PLC.
Сначала уточните, использует ли документация 0-based или 1-based notation, и преобразуйте
его в offset, который ожидает Modbus-библиотека.

Вкладка отдельно показывает вычисленный адрес для Client и Server. Базы адресов берутся
из `ModbusDemo.Client` и `ModbusDemo.Server`, потому что именно экран `Modbus Demo`
владеет TCP endpoint и lifecycle. Сохраняется при этом только `Modbus.DataMap`.

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
