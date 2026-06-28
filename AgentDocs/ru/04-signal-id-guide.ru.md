# SignalId guide

`SignalId` — стабильное доменное имя сигнала. Оно описывает смысл, а не физический адрес
PLC. RouteMap, ViewModel и XAML должны знать только `SignalId`, role, direction и value type.

## Формат

Рекомендуемый формат:

```text
<область>.<объект>.<свойство или команда>
```

Примеры:

```text
system.mode.automatic
system.mode.manual
system.emergency
route.node.bsu_1.active
route.node.bsu_1.target
route.node.bsu_1.loader
route.bsu2_to_bucket.fragment_1.active
equip.bucket.start
equip.bucket.stop
equip.bucket.text
connection.status
connection.connected
```

Используйте латинские буквы в нижнем регистре, цифры, точки и `_` внутри объекта.
Не используйте физические адреса:

```text
coil_15
register_40003_bit_2
plc1.db20.value
```

## Binding

Каждый binding содержит:

| Поле | Назначение |
|---|---|
| `Role` | Как RouteMap использует сигнал |
| `SignalId` | Доменное имя |
| `Direction` | `Read`, `Write` или `ReadWrite` |
| `ValueType` | Ожидаемый тип значения |

Direction должен соответствовать Modbus access:

| Direction | Требуемый access |
|---|---|
| `Read` | `Read` или `ReadWrite` |
| `Write` | `Write` или `ReadWrite` |
| `ReadWrite` | `ReadWrite` |

## Основные роли

| Role | Тип | Обычно |
|---|---|---|
| `Visible` | `Bool` | read-only visibility |
| `Fault` | `Bool` | read-only alarm state |
| `ActiveRoute` | `Bool` | read-only route highlight |
| `ActiveRouteFragment` | `Bool` | read-only split-line highlight |
| `Text` | `String` или число | card status |
| `Value` | любой поддержанный | extra runtime value |
| `StartCommand`, `StopCommand` | `Bool` | equipment commands |
| `TargetCommand`, `LoaderCommand` | `Bool` | node menu commands |
| `AutomaticModeCommand`, `ManualModeCommand`, `EmergencyCommand` | `Bool` | TopBar commands |

`State` и `*OffFeedback` — legacy. Не используйте их в новом поведении.

## Системные SignalId

| SignalId | Тип | Назначение |
|---|---|---|
| `connection.status` | `String` | Текст состояния Modbus runtime для TopBar |
| `connection.connected` | `Bool` | `true`, когда Modbus runtime running и snapshot не stale |

Системные SignalId создает provider. Они отображаются в `SignalId ↔ Modbus` как
системные строки и не добавляются в `Modbus.DataMap`.

## Типы

| SignalValueType | Modbus Type |
|---|---|
| `Bool` | `Bool` |
| `UInt16` | `UInt16` |
| `Int16`, `Int32` | `Int` |
| `Float32` | `Real` |
| `String` | `String` |

## Добавление нового сигнала

1. Добавьте binding в редакторе RouteMap или в seed/configuration mapper.
2. Выберите role, direction и value type.
3. Примените или сохраните RouteMap definition.
4. Откройте `SignalId ↔ Modbus`.
5. Создайте точку в `Modbus.DataMap` с тем же `Name`.
6. Настройте area, offset/physical address, bit, access, type и write mode.
7. Проверьте diagnostics, первый snapshot и readback.

SignalId не меняется при переносе сигнала на другой coil/register/bit. Меняется только
`Modbus.DataMap`.

Операторские аварии и предупреждения, которые не являются binding-ролями RouteMap,
не получают `SignalId` и не добавляются в `Modbus.DataMap`. Для них используйте
admin-вкладку `Менеджер тревог`, которая сохраняет `Modbus.AlarmMap`.

В таблице `Менеджер тревог` `Alarm area/Offset/Bit` задают входной бит показа диалога,
а `OK area/Offset/Bit` — отдельный acknowledgement-бит для кнопки `Хорошо`.
Колонки `Alarm client/server` и `OK client/server` показывают физические адреса по
базам `ModbusDemo.Client/Server` и могут пересчитать area/offset при ручном вводе.
`Repeat ms` задает повтор диалога при сохраняющемся `Alarm=true`, `Pulse ms` — длину
импульса `true/false`. Для `HoldingRegister` `Bit` обязателен и равен `0..15`, для
`Coil` не используется.
