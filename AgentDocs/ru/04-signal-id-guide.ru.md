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
system.reset
system.emergency
system.command.unload
system.command.rotate
system.command.load
system.fault
route.node.bsu_1.active
route.node.bsu_1.target
route.node.bsu_1.loader
route.bsu2_to_bucket.fragment_1.active
equip.bucket.start
equip.bucket.power
equip.bucket.start.off
equip.bucket.stop
equip.bucket.stop.off
equip.bucket.enabled
equip.bucket.text
equip.bucket.parameter
connection.status
connection.connected
```

`system.mode.automatic`, `system.mode.manual` и `equip.bucket.selector.on/off` —
исторические имена. Они не входят в активный inventory новой карточки и TopBar;
старые поля JSON читаются только для совместимости.

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
| `Text` | `UInt16` / `Word` с таблицей статусов, `String` или число для legacy | Технологический статус карточки |
| `Value` | любой поддержанный | extra runtime value |
| `EquipmentParameter` | любой поддержанный | настраиваемый параметр оборудования карточки |
| `StartCommand` | `Bool` | моментальный `ПУСК`, одна команда `true`; Modbus Pulse выполняет `false` |
| `StopCommand` | `Bool` | `СТОП` Toggle, Latched `true/false` и readback |
| `PowerFeedback` | `Bool` | read-only индикатор питания; good/fresh 0 красный, 1 зелёный, иначе unknown |
| `UncheckedCommand`, `CheckedCommand` | `Bool` | только исторические карточки без `PowerFeedback` |
| `StartOffFeedback`, `StopOffFeedback` | `Bool` | read-only disable bits for card start/stop buttons |
| `TargetCommand`, `LoaderCommand` | `Bool` | node menu commands |
| `ResetCommand`, `EmergencyCommand` | `Bool` | действующие команды TopBar |
| `UnloadCommand`, `RotationCommand`, `LoadCommand` | `Bool` | моментальные команды TopBar в `admin`, `ReadWrite/Pulse` |
| `AutomaticModeCommand`, `ManualModeCommand` | `Bool` | obsolete, сохранены только в исторических DTO |
| `Enabled` | `Bool` | optional read-only availability of a configured element |

`StartOffFeedback=true` или `StopOffFeedback=true` отключает соответствующую кнопку
карточки; для исторических Toggle-карточек checked снимается без записи команды. Остальные
`*OffFeedback` и `State` — legacy. Не используйте их в новом поведении.

Карточка с `statusMapping` получает числовой код через существующую привязку
`Text` (`Read/UInt16` или `Read/Word`). У «Кюбель Л.К.» это `equip.bucket.text`.
Сама таблица, тексты, цвета и категории хранятся в JSON карточки Route Map.
Физический адрес принадлежит точке `Modbus.DataMap` с тем же SignalId. Добавление
строки таблицы меняет интерпретацию кода и не создаёт регистр или дополнительный SignalId.
Валидатор Route Map проверяет наличие и контракт `Text`, а
`RouteMapModbusBindingDiagnostics` сообщает об отсутствующей точке, недостаточном
доступе или несовместимом типе в `Modbus.DataMap`.

`system.fault` — отдельный read-only сигнал общей аварии. `Modbus.AlarmMap`
обслуживает операторские тревоги и подтверждения независимо от таблиц статусов.
Порядок индикации и настройки описан в [руководстве Route Map](02-route-map-guide.ru.md).

`EquipmentParameter` используется только для списка параметров оборудования карточки.
Администратор задает `Title`, `SignalId`, `Direction` и `ValueType` во вкладке
`Карточки`; такие SignalId автоматически появляются в `SignalId ↔ Modbus`, а
физический адрес по-прежнему задается только в `Modbus.DataMap`.

Для новой карточки «Кюбель Л.К.» `PowerFeedback` использует `Read/Bool`
`equip.bucket.power`; Start требует Pulse, Stop — Latched. Физический адрес Power
нужно назначить по PLC-контракту, не копируя старый selector.
`UncheckedCommand` и `CheckedCommand` остаются лишь для исторических карточек без
`PowerFeedback` и не создаются для новой карточки.
`ResetCommand` обязателен для TopBar-кнопки `СБРОС`, по умолчанию использует
`system.reset` и требует `ReadWrite/Bool/Pulse`. У `СБРОС`, `АВАРИЯ` и объектов карты
хороший `Enabled=false` отключает элемент. Отсутствие привязки или значения не
ограничивает его по `Enabled`, Bad/Stale блокирует. Для карточки с `PowerFeedback` хороший
`Enabled=false` не отправляет selector-записей.

## Командные SignalId TopBar

| SignalId по умолчанию | Роль | Категория | Тип / доступ / режим записи |
|---|---|---|---|
| `system.command.unload` | `UnloadCommand` | `TopBar` | `Bool / ReadWrite / Pulse` |
| `system.command.rotate` | `RotationCommand` | `TopBar` | `Bool / ReadWrite / Pulse` |
| `system.command.load` | `LoadCommand` | `TopBar` | `Bool / ReadWrite / Pulse` |

`SignalId ↔ Modbus` показывает эти три строки и настроенные `Enabled`, по одной
строке на уникальный SignalId. Администратор может изменить SignalId во вкладке
`TopBar` редактора Route Map.
Каждому имени нужна вручную назначенная точка `Modbus.DataMap` с таким же `Name`.
`AutomaticModeCommand` и `ManualModeCommand` остаются в исторических JSON-привязках
и исключены из активного списка.

На каждую кнопку можно настроить один `Enabled` (`Read/Bool`). Это разрешающий
вход без `Pulse`, для подтверждения механического выполнения нужен отдельный
технологический feedback. Если привязки нет, команда проверяется по остальным
условиям. Если она есть, требуется свежий `Good/true`; `false`, Bad/Stale,
отсутствующее значение или неверный тип блокируют выполнение.

### Диагностика сопоставления команд TopBar

| Ситуация | Что показывает или проверяет приложение |
|---|---|
| Точка отсутствует | Строка `Не настроен`. В Modbus-режиме кнопка недоступна, dispatcher отклоняет запрос без точки. |
| Неверный тип | Строка `Ошибка` с сообщением `Ожидается Bool, выбран несовместимый Modbus тип …`. Запись отклоняется. |
| Точка только для чтения | Строка `Ошибка` с сообщением `Binding требует доступ ReadWrite, выбран Read.` Запись отклоняется. |
| `Latched` вместо `Pulse` | Строка `Ошибка` с сообщением `SignalId … требует режим записи Pulse.` Dispatcher проверяет обязательный режим до записи. |
| Некорректный физический адрес | Строка `Ошибка`. Валидатор проверяет offset, bit и диапазоны активных endpoint (например, `ModbusRegisterBitInvalid` или `ModbusRegisterRangeInvalid`). Некорректная карта отклоняется. |
| Пересечение точек | Проверка всей карты при сохранении выдаёт `Modbus data points '…' and '…' use overlapping … addresses.` (`ModbusDataPointAddressConflict`). Это блокирует повторное использование одного бита или перекрытие целого слова и бита. |

TopBar блокирует команду при отсутствии точки или несовместимом типе, доступе и
режиме записи. Проверка карты и активной точки перед записью дополнительно
отклоняет некорректную адресацию. Об условиях назначения точки см. [руководство Modbus TCP](03-modbus-tcp-guide.ru.md).

## Привязка ответа ПЛК

`CommandResetFeedback` относится к «ВЫГРУЗКА», «ВРАЩЕНИЕ», «ЗАГРУЗКА» или
моментарному «ПУСК». При добавлении редактор предлагает `<SignalId команды>.reset`.
Используйте отдельный SignalId на команду и настройте его в **SignalId ↔ Modbus**
как `Read/Bool` с длиной 1. Адрес выбирается по проекту ПЛК. В Holding Register
укажите бит 0–15 в слове без записей приложения. [Контракт обмена и ограничения](03-modbus-tcp-guide.ru.md#приоритет-и-ответ-плк).

## Системные SignalId

| SignalId | Тип | Назначение |
|---|---|---|
| `connection.status` | `String` | Текст состояния Modbus runtime для TopBar |
| `connection.connected` | `Bool` | `true`, когда Modbus runtime running и доступен свежий snapshot текущего подключения |
| `system.fault` | `Bool` | PLC-mapped общий сигнал аварии RouteMap; `true` переводит элементы карты в fault-вид |

`connection.status` и `connection.connected` создает provider: они отображаются в
`SignalId ↔ Modbus` как внутренние системные строки и не добавляются в `Modbus.DataMap`.
`system.fault` отображается в той же системной группе, но это обычный read-only Bool из
PLC: создайте для него точку `Modbus.DataMap`.

## Типы

| SignalValueType | Modbus Type |
|---|---|
| `Bool` | `Bool` |
| `UInt16` | `UInt16` |
| `Word` | `Word` (`UInt16` остается совместимым для старых конфигов) |
| `Int16`, `Int32` | `Int` |
| `Dword` | `Dword` |
| `Float32` | `Real` |
| `String` | `String` |
| `Date` | `Date` |

## Добавление нового сигнала

1. Добавьте binding в редакторе RouteMap или в seed/configuration mapper.
2. Выберите role, direction и value type.
3. Примените или сохраните RouteMap definition.
4. Откройте `SignalId ↔ Modbus`.
5. Создайте точку в `Modbus.DataMap` с тем же `Name`.
6. Настройте area, offset/physical address, bit, access, type и write mode.
7. Проверьте diagnostics, первый snapshot и readback.

Для параметров оборудования добавляйте сигнал через секцию карточки
`Настройки оборудования`. `Read` параметры показываются в диалоге только для чтения,
`Write` и `ReadWrite` отправляются через `SignalWriteRequest` при нажатии
`Сохранить`. Bool-параметры вводятся переключателем, остальные значения должны
соответствовать `SignalValueType`; `Word` имеет отдельный Modbus type `Word` и
совместим со старым `UInt16`, `Dword` — это unsigned 32-bit, `Date` передается как
`DateTime`. Для Modbus-источника строка должна быть настроена в `SignalId ↔ Modbus` до
отправки, а `String` требует достаточного `Length`.

`Visibility` задаёт отображение параметра в операторском диалоге. Сигнальная роль
остаётся `EquipmentParameter`, а `SignalId`, точка в `Modbus.DataMap` и адрес ПЛК
сохраняются при смене видимости. Полная сигнальная модель и фоновая обработка
ожидающих уставок учитывают обе категории видимости.

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
