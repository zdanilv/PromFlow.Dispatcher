# Modbus TCP guide

RouteMap использует Modbus TCP через доменные `SignalId`. Физическая адресация находится
в `Modbus.DataMap`, а endpoint и lifecycle принадлежат `ModbusDemo`.

Рабочие секции `Modbus` и `ModbusDemo` сохраняются в общем
`%LOCALAPPDATA%\Configurator\appsettings.json`. В `admin` режиме они доступны через
вкладки `SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus Demo`; в `user` режиме эти
вкладки скрыты, но runtime, autostart, mapping и тревоги продолжают использовать те же
сохраненные значения.

## Разделение карт

| Карта | Назначение |
|---|---|
| `Modbus.DataMap` | Production mapping RouteMap `SignalId` к coils/registers/bits |
| `Modbus.AlarmMap` | User-диалоги аварий/повторных подтверждений и acknowledgement-биты |
| `ModbusDemo.DataMap` | Только controls экрана `Modbus Demo` |

Не добавляйте RouteMap SignalId в `ModbusDemo.DataMap`. Не используйте demo-точки как
production mapping.
Не добавляйте операторские тревоги в `Modbus.DataMap`: они настраиваются отдельно в
`Modbus.AlarmMap`, чтобы RouteMap не видел их как SignalId.
Исключение по названию, но не по смыслу: `system.fault` — это не операторский диалог, а
системный RouteMap SignalId общей аварии; его нужно настраивать в `Modbus.DataMap`.

## Endpoint и lifecycle

`ModbusDemo` задает:

- Client host, port, UnitId;
- Server bind address, port, UnitId;
- coil/register start addresses и counts;
- poll interval;
- autostart и startup mode.

Переключение RouteMap на `SignalSource=Modbus` не запускает TCP runtime. Runtime запускается
на вкладке `Modbus Demo` или через `ModbusDemo.AutostartOnWorkspaceOpen`.

## DataMap point

Пример coil:

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

Пример bool в holding register:

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

`Name` должен точно соответствовать `SignalBinding.SignalId`. Сравнение выполняется без
учета регистра, но используйте единое написание.

Параметры оборудования из карточки RouteMap используют роль `EquipmentParameter` и также
попадают в `SignalId ↔ Modbus` как обычные доменные `SignalId`. Их `Direction` задает
требуемый `Access`, а `ValueType` — ожидаемый Modbus type: `Word` пишется как `Word`,
legacy `UInt16` — как `UInt16`, `Dword` — как `Dword`, `Date` — как `Date`. Физический
адрес задается только в `Modbus.DataMap`.

## AlarmMap point

Тревога задает отдельный alarm-бит и отдельный acknowledgement-бит:

```json
{
  "Id": "alarm.main",
  "Enabled": true,
  "Kind": "Fault",
  "Message": "Авария привода",
  "Alarm": { "Area": "HoldingRegister", "Address": 5, "BitIndex": 0 },
  "Acknowledgement": { "Area": "HoldingRegister", "Address": 5, "BitIndex": 1 },
  "RepeatIntervalMs": 60000,
  "AcknowledgementPulseDurationMs": 300
}
```

`ModbusAlarmMonitor` работает при открытом Workspace и в `admin`, и в `user` режиме.
Он показывает диалог на фронте `Alarm=true`. Кнопка `Хорошо` пишет
acknowledgement-импульс `true/false` только если alarm-бит все еще активен; если
alarm-бит остается `true`, диалог повторяется через `RepeatIntervalMs`.
После показа диалога тревога добавляется во вкладку `Уведомления` правой панели RouteMap.
`Хорошо` снимает маркер непрочитанного, а `X` элемента списка удаляет его только после
`Alarm=false`. `Очистить список` удаляет только такие же неактивные уведомления и не
пишет acknowledgement. События активации, снятия и `OK` пишутся в сессионную вкладку
`История`; это in-memory журнал, а не сохраненная БД.

### Таблица `Менеджер тревог`

Вкладка редактирует только `Modbus.AlarmMap`. Кнопка `ДОБАВИТЬ` создает включенную
строку с типом `Fault`, `Alarm=Coil[0]`, `Acknowledgement=Coil[1]`,
`RepeatIntervalMs=60000` и `AcknowledgementPulseDurationMs=300`. `СОХРАНИТЬ` записывает
только `AlarmMap`, не меняя `DataMap`; `ПЕРЕЗАГРУЗИТЬ` перечитывает сохраненную карту и
сбрасывает локальный черновик.

| Колонка | Поле конфигурации | Назначение |
|---|---|---|
| `Вкл.` | `Enabled` | Включает строку для монитора тревог; выключенная строка сохраняется, но не показывает диалог и не пишет acknowledgement |
| `Id` | `Id` | Уникальный идентификатор тревоги; используется для состояния повтора и должен быть непустым |
| `Тип` | `Kind` | `Fault` показывает красный диалог `Авария`; `Confirmation` показывает предупреждающий диалог `Повторное подтверждение`; `Message` показывает нейтральный диалог `Сообщение` |
| `Сообщение` | `Message` | Текст, который оператор видит в модальном диалоге |
| `Alarm area` | `Alarm.Area` | Область входного бита: `Coil` или `HoldingRegister` |
| `Offset` после `Alarm area` | `Alarm.Address` | Zero-based offset alarm-бита внутри выбранной области |
| `Bit` после `Alarm area` | `Alarm.BitIndex` | Бит `0..15` для `HoldingRegister`; для `Coil` не используется |
| `Alarm client` | вычисляется из `Alarm.*` и `ModbusDemo.Client` | Физический адрес alarm-бита для client start address; ввод пересчитывает `Alarm.Area` и `Alarm.Address` |
| `Alarm server` | вычисляется из `Alarm.*` и `ModbusDemo.Server` | Физический адрес alarm-бита для server start address; ввод пересчитывает `Alarm.Area` и `Alarm.Address` |
| `OK area` | `Acknowledgement.Area` | Область отдельного acknowledgement-бита, куда пишет кнопка `Хорошо` |
| `Offset` после `OK area` | `Acknowledgement.Address` | Zero-based offset acknowledgement-бита |
| `Bit` после `OK area` | `Acknowledgement.BitIndex` | Бит `0..15` для acknowledgement в `HoldingRegister`; для `Coil` не используется |
| `OK client` | вычисляется из `Acknowledgement.*` и `ModbusDemo.Client` | Физический адрес acknowledgement-бита для client start address |
| `OK server` | вычисляется из `Acknowledgement.*` и `ModbusDemo.Server` | Физический адрес acknowledgement-бита для server start address |
| `Repeat ms` | `RepeatIntervalMs` | Интервал повторного показа, пока alarm-бит остается `true`; допустимо `1000..86400000` |
| `Pulse ms` | `AcknowledgementPulseDurationMs` | Длительность acknowledgement-импульса `true/false`; допустимо `1..60000` |
| `Действие` | — | `Копия` дублирует строку с новым `Id`; `Удалить` удаляет строку из черновика |

`Alarm` и `Acknowledgement` должны указывать на разные биты и попадать в диапазоны
активных endpoint из `ModbusDemo`. Закрытие диалога кнопкой `X` не пишет
acknowledgement; импульс отправляется только по `Хорошо`, пока alarm-бит активен.

## Address и physical address

`DataMap.Address` — zero-based offset относительно start address активного endpoint.

```text
physical coil address     = CoilStartAddress + Address
physical register address = HoldingRegisterStartAddress + Address
```

Перед вводом production-адресов уточните, использует ли PLC-документация 0-based,
1-based или notation вроде `40001`.

## Snapshots и quality

Клиент или сервер публикует `ModbusSnapshot`. RouteMap facade декодирует snapshot по
`Modbus.DataMap` и публикует значения. Если runtime остановлен, связь потеряна или значение
устарело, RouteMap получает bad/stale quality, а не аварийное завершение приложения.

`connection.status` и `connection.connected` — системные SignalId. Их создает runtime
provider; добавлять их в `DataMap` не нужно. `connection.connected=false` блокирует
команды RouteMap, переводит узлы/линии в offline-состояние и показывает на карточках
текст `Не в сети` независимо от status/text binding.

`system.fault` — системный, но PLC-mapped SignalId. Он отображается в `SignalId ↔ Modbus`
как строка системной группы, допускает создание точки `Modbus.DataMap` с `Read/Bool` и
при `true` переводит RouteMap-объекты и карточки в общий аварийный вид.

## Запись

`ModbusTcpCommandDispatcher` проверяет:

- наличие `SignalId` в `Modbus.DataMap`;
- writable access;
- совместимость `SignalValueType` и Modbus type;
- корректность значения и адреса.

Диалог параметров карточки отправляет значения тем же путем, что `ПУСК`/`СТОП`:
`Write` и `ReadWrite` параметры формируют `SignalWriteRequest`; `Read` параметры не
пишутся и только отображают последний хороший snapshot. Bool вводится переключателем
`Вкл/Выкл`; `Word`/`UInt16` принимают `0..65535`, `Dword` — `0..4294967295`,
`Date` — дату/время в текущей или invariant culture, остальные типы парсятся по
`SignalValueType`. Перед `DispatchAsync` диалог проверяет, что строка `SignalId` есть в
`Modbus.DataMap`, access допускает запись, Modbus type совместим с `SignalValueType`, а
`String` помещается в емкость `Length * 2` UTF-8 байт.

`Latched` записывает переданное значение и для readable-точек ждет readback до
`WriteConfirmationTimeoutMs`.

`Pulse` принимает только `true`, пишет `true`, ждет `PulseDurationMs`, затем пытается
записать `false`. Используйте pulse только для PLC-контрактов, где нужен фронт или
короткий импульс.

## Register-bit write

Bool внутри holding register пишется как сериализованный read-modify-write. Перед первой
записью должен быть получен raw snapshot слова; иначе возвращается
`ModbusRegisterShadowUnavailable`.

Не назначайте один и тот же bit двум SignalId. Не пересекайте целое register-значение со
словом, которое используется как набор bit-точек.
