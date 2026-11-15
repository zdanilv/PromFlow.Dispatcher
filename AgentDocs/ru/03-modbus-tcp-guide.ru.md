# Modbus TCP guide

RouteMap использует Modbus TCP через доменные `SignalId`. Физическая адресация находится
в `Modbus.DataMap`, а endpoint и lifecycle принадлежат экрану `Modbus TCP`
(внутренняя совместимая секция конфигурации — `ModbusDemo`).

Рабочие секции `Modbus` и `ModbusDemo` сохраняются в общем
`%LOCALAPPDATA%\Configurator\appsettings.json`. В `admin` режиме они доступны через
вкладки `SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus TCP`; в `user` режиме эти
вкладки скрыты, но runtime, autostart, mapping и тревоги продолжают использовать те же
сохраненные значения.

## Разделение карт

| Карта | Назначение |
|---|---|
| `Modbus.DataMap` | Production mapping RouteMap `SignalId` к coils/registers/bits |
| `Modbus.AlarmMap` | User-диалоги аварий/повторных подтверждений и acknowledgement-биты |
| `ModbusDemo.DataMap` | Legacy-данные прежнего demo-экрана; новый UI и экспорт профиля их не используют |

Не добавляйте RouteMap SignalId в `ModbusDemo.DataMap`. Production mapping всегда
настраивается через `Modbus.DataMap`.
Не добавляйте операторские тревоги в `Modbus.DataMap`: они настраиваются отдельно в
`Modbus.AlarmMap`, чтобы RouteMap не видел их как SignalId.
Исключение по названию, но не по смыслу: `system.fault` — это не операторский диалог, а
системный RouteMap SignalId общей аварии; его нужно настраивать в `Modbus.DataMap`.

## Endpoint и lifecycle

`Modbus TCP` задает:

- Client host, port, UnitId;
- Server bind address, port, UnitId;
- coil/register start addresses и counts;
- poll interval;
- autostart и startup mode.

Переключение RouteMap на `SignalSource=Modbus` не запускает TCP runtime. Runtime запускается
на вкладке `Modbus TCP` или через `ModbusDemo.AutostartOnWorkspaceOpen`.

## Импорт и экспорт профиля

Кнопки `ИМПОРТ ПРОФИЛЯ` и `ЭКСПОРТ ПРОФИЛЯ` на административных вкладках
переносят один versioned JSON-файл `modbus-tcp-profile.json`. В нём находятся
общие TCP-настройки, `Modbus.DataMap` и `Modbus.AlarmMap`; legacy
`ModbusDemo.DataMap` намеренно не входит в файл.

Перед импортом проверяются формат/версия JSON, структурные правила карт и диапазоны
Client/Server. Если карты требуют больше Coils или Holding Registers, UI предлагает
расширить count до минимального значения и включить используемую область либо отменить
операцию. При подтверждении обе секции `Modbus` и `ModbusDemo` сохраняются вместе;
endpoint вступают в силу после запуска или перезапуска роли.
Профиль со старыми `system.mode.automatic`/`system.mode.manual` отклоняется с
диагностикой: удалите только эти записи из копии профиля и повторите импорт.
Экспорт также требует предварительной очистки сохранённой карты через редактор.

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

Сохранённые точки без активной привязки RouteMap показаны в группе
**«Неиспользуемые точки»**. Чтобы убрать такую точку из `Modbus.DataMap`, остановите
клиент и сервер Modbus TCP, нажмите **«Удалить»** и подтвердите действие. Точка
удаляется сразу, в том числе если её настройки содержат ошибку. Явно сохранённый
пользовательский массив `Modbus.DataMap`, включая пустой массив, заменяет карту из
установочных настроек целиком.

Карточка «Кюбель Л.К.» использует три независимые точки: `equip.bucket.power` —
`Read/Bool` без записи; `equip.bucket.start` — `ReadWrite/Bool/Pulse` с длительностью
300 мс по умолчанию; `equip.bucket.stop` — `ReadWrite/Bool/Latched` с записями
`true` и `false` по действию Toggle. Старые `selector.on/off` не переносятся на
`power`: их физический адрес не доказывает, что это feedback питания. Для
исторических карточек без `PowerFeedback` прежняя selector-логика сохранена.

`system.mode.automatic` и `system.mode.manual` — obsolete. Их нет в поставляемом
`Modbus.DataMap`, активном inventory и UI. При сохранении через `SignalId ↔ Modbus`
редактор удаляет только эти два точных имени из сохранённой карты. Если другой
активный binding использует то же имя, операция останавливается с диагностикой.
Очистка требует остановленных client/server ролей; `SaveSectionAsync` сохраняет
резервную копию существующего пользовательского `appsettings.json` с суффиксом
`.before-mode-cleanup.<id>.bak` перед удалением и сохраняет неизвестные поля
оставшихся точек. После очистки карта применяется при следующем запуске роли;
редактор не заменяет runtime-карту во время возможного импульса. Повторное
сохранение не удаляет другие `system.*` и не возвращает obsolete IDs.

TopBar-кнопка `СБРОС` создает строку `ResetCommand` с SignalId `system.reset`.
Это `Bool / ReadWrite / Pulse` команда: UI пишет только `true`, а Modbus dispatcher по
`PulseDurationMs` выполняет сброс `true -> false`. Состояние кнопки берется из
momentary/readback-индикатора; `Latched` для `ResetCommand` отклоняется.

Команды TopBar `ВЫГРУЗКА`, `ВРАЩЕНИЕ` и `ЗАГРУЗКА` по умолчанию используют
`system.command.unload`, `system.command.rotate` и `system.command.load`. Каждая
требует `Type=Bool`, `Access=ReadWrite`, `WriteMode=Pulse`. `TopBarViewModel`
отправляет один запрос `true` с `RequiredWriteMode = ModbusWriteMode.Pulse`.
Обычный `Button` запускает импульс по нажатию. За задержку и запись `false`
отвечает dispatcher. Режим `Latched` для этих запросов отклоняется до записи.
`СБРОС` также требует `Pulse`. `АВАРИЯ` сохраняет `ToggleButton` и штатный
`Latched` с отдельными `true/false` по состоянию кнопки. Для пользовательского
профиля проверьте `Latched` у `АВАРИЯ` отдельно. Её ViewModel отправляет `true/false`
без требования конкретного `RequiredWriteMode`. Операторские тревоги и их
подтверждения обслуживает `Modbus.AlarmMap`.

Для каждой новой команды нажмите `Создать` в её строке `SignalId ↔ Modbus` и
вручную настройте точку с соответствующим `Name`. В `Modbus.DataMap` задаются
`Area`, zero-based `Address` (`Offset`), `Length=1` и `BitIndex` (`Bit`, обязателен для `HoldingRegister`,
пуст для `Coil`). До согласования физической точки оставьте строку `Не настроен`.
Route Map хранит только доменную привязку, а поставляемая конфигурация не назначает
физические точки новым SignalId. Не выбирайте свободный бит без согласования с
PLC-разработчиком и не переносите синтетические тестовые адреса в промышленный профиль.

`PulseDurationMs` (`Pulse ms`) задаётся для каждой точки отдельно. Значение 300 мс
допустимо как пример для изолированного тестового профиля. Для оборудования
длительность согласуется по PLC-контракту.

Роль `Enabled` создает обычную строку `Read/Bool`. Ее физический адрес, как и у других
RouteMap SignalId, задается только в `Modbus.DataMap`.

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
  "AcknowledgementEnabled": true,
  "RepeatIntervalMs": 60000,
  "AcknowledgementPulseDurationMs": 300
}
```

`ModbusAlarmMonitor` работает при открытом Workspace и в `admin`, и в `user` режиме.
Он показывает диалог на фронте `Alarm=true`. Кнопка `Хорошо` отмечает уведомление
подтвержденным и, если включен `AcknowledgementEnabled`, пишет acknowledgement-импульс
`true/false` только когда alarm-бит все еще активен; если
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
| `OK feedback` | `AcknowledgementEnabled` | Включает запись acknowledgement-импульса после `Хорошо`; старые профили по умолчанию имеют значение `true` |
| `OK area` | `Acknowledgement.Area` | Область отдельного acknowledgement-бита, куда пишет кнопка `Хорошо` |
| `Offset` после `OK area` | `Acknowledgement.Address` | Zero-based offset acknowledgement-бита |
| `Bit` после `OK area` | `Acknowledgement.BitIndex` | Бит `0..15` для acknowledgement в `HoldingRegister`; для `Coil` не используется |
| `OK client` | вычисляется из `Acknowledgement.*` и `ModbusDemo.Client` | Физический адрес acknowledgement-бита для client start address |
| `OK server` | вычисляется из `Acknowledgement.*` и `ModbusDemo.Server` | Физический адрес acknowledgement-бита для server start address |
| `Repeat ms` | `RepeatIntervalMs` | Интервал повторного показа, пока alarm-бит остается `true`; допустимо `1000..86400000` |
| `Pulse ms` | `AcknowledgementPulseDurationMs` | Длительность acknowledgement-импульса `true/false`; допустимо `1..60000` |
| `Действие` | — | `Копия` дублирует строку с новым `Id`; `Удалить` удаляет строку из черновика |

Когда `OK feedback` выключен, OK-поля и `Pulse ms` сохраняют введенные значения, но
не редактируются, не валидируются и не считаются используемыми адресами. Когда он
включен, `Alarm` и `Acknowledgement` должны указывать на разные биты и попадать в
диапазоны активных endpoint из `ModbusDemo`. Закрытие диалога кнопкой `X` не пишет
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

Для кнопок `СБРОС`, `АВАРИЯ` и объектов карты хороший `Enabled=false` отключает
связанный элемент. Отсутствующее значение оставляет его доступным по `Enabled`.
Bad/Stale переводит объект в offline, а для кнопки TopBar блокирует команду.
У трёх новых команд TopBar настроенный `Enabled` должен дать свежий `Good/true`.
Отсутствующее или невалидное значение такого сигнала тоже блокирует кнопку.
При отсутствии самой привязки отдельной проверки `Enabled` нет.
У карточки `Enabled=false` не блокирует кнопку `С`, пока `connection.connected=true`;
при первом хорошем `false` карточка один раз пишет `CheckedCommand=false`, затем
`UncheckedCommand=false`.

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
записать `false`. После ошибки подтверждения `true`, timeout или отмены выполняется
ограниченная попытка сброса `false` с отдельным timeout; полная потеря сети не даёт
гарантии физического сброса. Не ждать обязательного readback `true` для короткого
импульса. Используйте pulse только для PLC-контрактов, где нужен фронт или
короткий импульс.
Не добавляйте автоматический повтор потенциально неидемпотентных команд после timeout
или потери связи: сначала установите фактическое состояние по readback и PLC-контракту.

Успех записи означает результат Modbus-операции. Физическое выполнение определяется
PLC и технологическим feedback. Ошибка записи `true` оставляет активацию
неподтверждённой: PLC мог получить запись до потери ответа. Ошибка записи или
подтверждения `false` оставляет состояние управляющего бита неопределённым.
Приложение не повторяет `true` автоматически после таймаута или reconnect.

### Условия подключения к действующему PLC

До подключения согласуйте и проверьте следующие условия с ответственным за PLC-контракт.

1. Физическую точку каждой команды с `Area`, register/coil, `BitIndex`, базами адресов,
   `UnitId`, тип `Bool`, доступ `ReadWrite` и `PulseDurationMs`.
2. Реакцию PLC на одиночный и повторный импульс, разрешающие условия каждой операции,
   аппаратные и программные межблокировки.
3. Безопасное поведение PLC и watchdog при потере связи и неопределённом `false`,
   защиту от повторного выполнения после reconnect и поведение оборудования после
   перезапуска приложения или PLC.
4. Порядок безопасного пробного включения и ответственного за утверждение контракта.

Окончательное разрешение на исполнение выдаёт PLC. Модульные и loopback-тесты
проверяют приложение и транспорт в изолированной среде. Для промышленного допуска
требуются отдельные испытания оборудования по утверждённому контракту.

## Register-bit write

Bool внутри holding register пишется как сериализованный read-modify-write. Перед первой
записью должен быть получен raw snapshot слова; иначе возвращается
`ModbusRegisterShadowUnavailable`.

Не назначайте один и тот же bit двум SignalId. Не пересекайте целое register-значение со
словом, которое используется как набор bit-точек.
