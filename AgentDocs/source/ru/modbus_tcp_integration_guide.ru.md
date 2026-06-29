# Подключение Modbus TCP к RouteMap

## Архитектура

RouteMap работает только с доменными `SignalId` и не хранит IP, UnitId или физические
адреса в `route-map.json`, XAML и ViewModel.

```text
ModbusDemo screen
  -> IModbusDemoTcpService
  -> shared ModbusRuntimeService / client / server

shared runtime snapshots
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> RouteMapRuntimeMapper
  -> UI

RouteMap UI commands
  -> ModbusTcpCommandDispatcher
  -> RouteMap IModbusTcpService facade
  -> shared ModbusRuntimeService / client / server
```

`ModbusDemo` является единственным экраном запуска, остановки и настройки Modbus TCP
подключения. `ModbusDemo.Client`, `ModbusDemo.Server`, `AutostartOnWorkspaceOpen` и
`StartupMode` задают endpoint и lifecycle.

Карты данных разделены:

- `ModbusDemo.DataMap` используется только контролами экрана `Modbus Demo`;
- `Modbus.DataMap` используется RouteMap и вкладкой `SignalId ↔ Modbus`;
- `Modbus.AlarmMap` используется монитором тревог в admin/user и вкладкой `Менеджер тревог`;
- эти карты читают и пишут через общий TCP runtime.

В `Application.WorkMode=admin` Workspace показывает вкладки `Route Map`,
`SignalId ↔ Modbus`, `Менеджер тревог`, `Modbus Demo`. В `Application.WorkMode=user`
Workspace показывает только `Route Map` на всю рабочую область; кнопка `НАСТРОЙКИ` в
TopBar скрыта, но диалоги из `Modbus.AlarmMap` продолжают работать; в admin они тоже
работают для наладки и симуляции.
`Application.WorkMode` читается как режим запуска оболочки, а рабочие секции
`RouteMapRuntime`, `Modbus` и `ModbusDemo` читаются и сохраняются в общем
`%LOCALAPPDATA%\Configurator\appsettings.json`. Поэтому admin настраивает подключение,
а user использует те же значения без видимых вкладок настройки.

## Выбор Источника RouteMap

```json
"RouteMapRuntime": {
  "SignalSource": "Mock",
  "StaleAfterMs": 1500
}
```

- `Mock` использует встроенную симуляцию и не требует PLC;
- `Modbus` использует общий demo runtime, но декодирует snapshot по `Modbus.DataMap`.

Источник переключается в `Route Map` -> `НАСТРОЙКИ` -> `Источник данных`.
`ПРИМЕНИТЬ` меняет текущую сессию, `СОХРАНИТЬ` также обновляет `RouteMapRuntime` в
общем `%LOCALAPPDATA%\Configurator\appsettings.json`. Переключение на Modbus не запускает соединение: запуск выполняется
на вкладке `Modbus Demo`.

Связи SignalId с адресами редактируются на вкладке `SignalId ↔ Modbus`.
Системные `connection.status` и `connection.connected` создает provider, в
`Modbus.DataMap` их не добавляют. `system.fault` находится в системной группе, но
настраивается как обычная read/bool точка `Modbus.DataMap`; при `true` вся RouteMap
переходит в общий аварийный вид, а offline остается выше по приоритету.

Тревоги, повторные подтверждения и обычные сообщения редактируются во вкладке `Менеджер тревог`. Они
сохраняются в `Modbus.AlarmMap`, используют отдельный alarm-bit и отдельный
acknowledgement-bit; кнопка `Хорошо` пишет acknowledgement-импульс.

### Таблица `Менеджер тревог`

Одна строка таблицы равна одной записи `Modbus.AlarmMap[]`.

| Колонка | Что задает | Как используется |
|---|---|---|
| `Вкл.` | `Enabled` | Включает/выключает строку без удаления из конфигурации |
| `Id` | `Id` | Уникальное имя тревоги; пустые и повторяющиеся значения запрещены |
| `Тип` | `Kind` | `Fault` открывает красный диалог аварии, `Confirmation` — предупреждающий диалог повторного подтверждения, `Message` — нейтральный диалог сообщения |
| `Сообщение` | `Message` | Текст сообщения в модальном диалоге |
| `Alarm area` | `Alarm.Area` | Где читать входной alarm-бит: `Coil` или `HoldingRegister` |
| `Alarm Offset` | `Alarm.Address` | Zero-based offset alarm-бита от start address выбранной области |
| `Alarm Bit` | `Alarm.BitIndex` | Номер бита `0..15` для `HoldingRegister`; для `Coil` пустой |
| `Alarm client/server` | физический адрес alarm-бита | Показывает и позволяет ввести адрес относительно `ModbusDemo.Client` или `ModbusDemo.Server`; ввод пересчитывает area/offset |
| `OK area` | `Acknowledgement.Area` | Где писать acknowledgement-бит после `Хорошо` |
| `OK Offset` | `Acknowledgement.Address` | Zero-based offset acknowledgement-бита |
| `OK Bit` | `Acknowledgement.BitIndex` | Номер бита `0..15` для acknowledgement в `HoldingRegister`; для `Coil` пустой |
| `OK client/server` | физический адрес acknowledgement-бита | Показывает адрес подтверждения для client/server start address |
| `Repeat ms` | `RepeatIntervalMs` | Повторный показ при сохраняющемся `Alarm=true`; диапазон `1000..86400000` мс |
| `Pulse ms` | `AcknowledgementPulseDurationMs` | Длина импульса `true/false` в acknowledgement-бит; диапазон `1..60000` мс |
| `Действие` | операции строки | `Копия` создает дубль с новым `Id`; `Удалить` убирает строку из черновика |

`Alarm` и `Acknowledgement` должны быть разными битами и попадать в диапазоны endpoint:
`CoilCount`/`RegisterCount` и flags `CoilsEnabled`/`HoldingRegistersEnabled`. Кнопка
`Хорошо` пишет acknowledgement; закрытие диалога через `X` только закрывает окно.

## Каталог SignalId

`ModbusDataPointOptions.Name` должен точно совпадать с `SignalBinding.SignalId`.
`Address` хранится как нулевое смещение относительно `ModbusDemo.Client` или
`ModbusDemo.Server` start address.

Обычный Coil:

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

Bool внутри Holding Register:

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

Для register-bit точки обязательны `Type=Bool`, `Length=1` и `BitIndex=0..15`.
Запись выполняется как сериализованный read-modify-write.

Отрезки длинных линий RouteMap v10 используют read-only роль `ActiveRouteFragment`.
Имена генерируются как `route.<segmentId>.fragment_<n>.active`, например
`route.bsu2_to_bucket.fragment_1.active`. Их можно маппить в разные coils или в разные
bits одного holding register; общий `Fault` линии остается отдельным сигналом на всю
линию.

## Семантика Команд

В schema v10 карточные `ПУСК`/`СТОП` и TopBar `АВАРИЯ` всегда работают как
toggle-команды RouteMap. `ПУСК` и `СТОП` взаимоисключаются: включение одной кнопки
сначала пишет `false` в противоположную команду, затем `true` в свою; ручное снятие
пишет только свою команду `false`. `StartOffFeedback`/`StopOffFeedback` — опциональные
read/bool роли карточек: `true` отключает кнопку и показывает ее снятой без записи в PLC.
Legacy-значение `RouteCommandButtonKind.Momentary` миграция приводит к `Toggle`.

`ModbusWriteMode` управляет только физической записью:

- `ModbusWriteMode.Latched` физически записывает переданное значение;
- `ModbusWriteMode.Pulse` физически пишет `true`, ждет `PulseDurationMs`, затем пишет `false`.

`Pulse` можно выбрать вручную для нужной точки `Modbus.DataMap`; новые mapping больше
не получают pulse-режим автоматически по типу кнопки.

## Ввод В Эксплуатацию

1. Оставить `SignalSource=Mock` и проверить RouteMap UI.
2. Настроить endpoint и lifecycle на вкладке `Modbus Demo`.
3. Заполнить `Modbus.DataMap` на вкладке `SignalId ↔ Modbus`.
4. Заполнить `Modbus.AlarmMap` на вкладке `Менеджер тревог`, если нужны диалоги тревог, повторных подтверждений или сообщений.
5. Заполнить `ModbusDemo.DataMap` только для контролов demo-экрана.
6. Запустить Client или Server на вкладке `Modbus Demo`.
7. Переключить RouteMap на `SignalSource=Modbus`.
8. Проверить readback, timeout, alarm acknowledgement, reconnect и interlock на стенде.

UI не является контуром функциональной безопасности. Interlock режимов, аварии и
исполнительных механизмов должен оставаться в PLC.

## Проверка

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```
