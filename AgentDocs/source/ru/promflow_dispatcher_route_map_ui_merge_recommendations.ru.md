# Актуальные рекомендации по RouteMap UI и Modbus TCP

## Статус

Целевой репозиторий: `PromFlow.Dispatcher`, ветка `3-route-map-merge`.
Исходный RouteMap: `DekstopTemplate`, ветка `11-route-map-3`.

Предыдущая версия документа ссылалась на `2-new-view` и описывала будущую интеграцию.
После слияния актуальна следующая архитектура.

## Что Перенесено

- `Configurator.Application/Services/Signals`;
- полный `Configurator.Desktop/Workspace/RouteMap`, включая schema v4, миграции,
  редактор настроек, mock runtime, карту и attached-карточки;
- 91 RouteMap unit-тест и 12 headless UI-тестов;
- programmer guide и Modbus integration guide.

Оболочка, `Program.cs`, `App`, `MainWindow`, Modbus, ModbusDemo и OPC UA сохранены из
`PromFlow.Dispatcher`. Обычное Git-слияние репозиториев не применялось.

## Итоговый Workspace

RouteMap является первой вкладкой и открывается по умолчанию. После него остаются:

```text
Route Map
SignalId ↔ Modbus
Менеджер тревог
Modbus Demo
```

`WorkspaceViewModel` владеет дочерними ViewModel и освобождает их подписки. При закрытии
приложения основной и demo Modbus runtime по-прежнему останавливаются.

## Интеграция С Modbus

```text
IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMap UI

RouteMap UI
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService
```

`IModbusTcpService` обратно совместим. Новый snapshot-контракт публикует данные на каждом
poll, тогда как `Subscribe` продолжает уведомлять только об изменениях.

Каталог находится в `Modbus.DataMap`, где `Name == SignalId`. Точки поддерживают:

```text
Area: Coil | HoldingRegister
BitIndex: 0..15 для Bool в Holding Register
WriteMode: Latched | Pulse
PulseDurationMs: длительность импульса
```

Register-bit запись выполняется сериализованным read-modify-write с сохранением соседних
битов. До получения первого raw snapshot запись такого бита отклоняется.
Операторские аварии и повторные подтверждения настраиваются отдельно в `Modbus.AlarmMap`,
чтобы не попадать в SignalId-каталог RouteMap.
В таблице `Менеджер тревог` `Alarm area/Offset/Bit` описывают входной бит, `OK area/Offset/Bit`
— отдельный acknowledgement-бит, `Repeat ms` — интервал повторного показа, `Pulse ms` —
длительность импульса подтверждения. Для `HoldingRegister` bit обязателен `0..15`, для
`Coil` не используется.

## Конфигурация Режима

```json
"RouteMapRuntime": {
  "SignalSource": "Mock",
  "StaleAfterMs": 1500
}
```

`Mock` остается безопасным значением по умолчанию. `Modbus` подключает основной стек;
`ModbusDemo` остается независимым и использует собственную секцию 16384+.

`Modbus.AutostartOnWorkspaceOpen` и `StartupMode` теперь реально применяются при создании
Workspace. Production-адреса PLC должны быть заменены в `Modbus.DataMap` без изменения
RouteMap JSON или XAML.
Alarm/acknowledgement-биты должны быть заменены в `Modbus.AlarmMap` без добавления
служебных SignalId.

## Эксплуатационные Ограничения

- UI не является контуром функциональной безопасности;
- interlock и атомарная PLC-семантика остаются в контроллере;
- `Pulse` назначается только командам с подтвержденной импульсной семантикой;
- режимы, маршрутные роли и toggle-команды по умолчанию остаются `Latched` с readback;
- отсутствующие SignalId, неверный доступ и несовпадение типов диагностируются в логах,
  но не завершают приложение.

## Проверка

```powershell
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore
```

Детали реализации находятся в:

- `Configurator.Desktop/route_map_programmer_guide.md`;
- `Configurator.Desktop/modbus_tcp_integration_guide.md`;
- `Configurator.Desktop/Описание ModbusDemo.md`;
- `Configurator.Desktop/ModbusDemo.Controls.md`.
