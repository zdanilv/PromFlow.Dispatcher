# Подключение Modbus TCP к RouteMap

## Архитектура

RouteMap работает только с доменными `SignalId`:

```text
ModbusTcpService
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> UI

UI
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService
```

IP, UnitId, адреса и номера битов находятся в `Configurator.Boot/appsettings.json`,
секция `Modbus.DataMap`. В `route-map.json`, XAML и ViewModel Modbus-адресов нет.

## Выбор Источника

```json
"RouteMapRuntime": {
  "SignalSource": "Mock",
  "StaleAfterMs": 1500
}
```

- `Mock` использует `MockSignalProvider` и не требует PLC;
- `Modbus` использует основной `IModbusTcpService`;
- `ModbusDemo` всегда остается отдельным стеком и отдельной секцией конфигурации.

Источник можно переключить без перезапуска в `Route Map` → `НАСТРОЙКИ` →
`Источник данных`. `ПРИМЕНИТЬ` меняет текущую сессию, `СОХРАНИТЬ` также обновляет
`RouteMapRuntime` в `appsettings.json`. Переключение на Modbus не запускает соединение.

Связи SignalId с физическими адресами редактируются на отдельной вкладке
`SignalId ↔ Modbus`. Подробности: `signal_id_modbus_tcp_mapping_guide.md`.

`Modbus.AutostartOnWorkspaceOpen` и `Modbus.StartupMode` применяются при создании
Workspace. Допустимы `None`, `Client`, `Server` и `Both`.

## Каталог SignalId

`ModbusDataPointOptions.Name` должен точно совпадать с `SignalBinding.SignalId`.
Адрес хранится как нулевое смещение относительно `CoilStartAddress` или
`HoldingRegisterStartAddress` endpoint.

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
  "WriteMode": "Latched"
}
```

Для register-bit точки обязательны `Type=Bool`, `Length=1` и `BitIndex=0..15`.
Запись выполняется как сериализованный read-modify-write. Последнее сырое слово
поддерживается из poll snapshot и после успешных записей, поэтому параллельные изменения
соседних битов не теряются. До получения первого слова bit-write отклоняется.

## Семантика Команд

`WriteMode` задается отдельно для каждой writable-точки:

- `Latched` записывает переданное `true/false` и для readable-точки ждет readback;
- `Pulse` обрабатывает запрос `true`, записывает `true`, ждет `PulseDurationMs`, затем
  гарантированно пытается записать `false`; входной запрос `false` игнорируется.

Значение по умолчанию — `Latched`, длительность импульса — `300 ms`. Не назначайте
`Pulse` режимам, ролям маршрута или toggle-кнопкам без подтвержденной PLC-семантики.

## Snapshots, Quality И Stale

`IModbusTcpService.Subscribe` сохраняет прежний контракт и уведомляет только при изменении
точки. `IModbusDataSnapshotSource` дополнительно публикует согласованный набор именованных
значений на каждом poll, даже если данные не изменились.

`ModbusTcpSignalValueProvider`:

- преобразует значения в `SignalValue`;
- публикует `connection.status` из `ModbusServiceState`;
- ставит плохое quality при `Stopped`, `Faulted` и `Reconnecting`;
- ставит `IsStale`, если snapshot старше `RouteMapRuntime.StaleAfterMs`;
- не обращается к Avalonia dispatcher: перевод на UI-поток выполняет dashboard.

При `SignalSource=Modbus` диагностика сопоставляет актуальные bindings RouteMap с
`Modbus.DataMap` и пишет предупреждения для отсутствующих SignalId, неправильного доступа
и несовместимого типа. Ошибка конфигурации не завершает приложение.

## Ввод В Эксплуатацию

1. Оставить `SignalSource=Mock` и проверить UI, настройки и JSON migrations.
2. Заполнить production `Modbus.DataMap`, не меняя RouteMap JSON.
3. Переключить `SignalSource=Modbus`, начать с read-only сигналов.
4. Сверить значения и bit order с диагностикой PLC.
5. Последовательно разрешить TopBar, роли узлов и ПУСК/СТОП.
6. Проверить readback, timeout, reconnect и interlock на стенде.

UI не является контуром функциональной безопасности. Interlock режимов, аварии и
исполнительных механизмов должен оставаться в PLC.

## Проверка

```powershell
dotnet build .\Configurator.Boot\Configurator.Boot.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter "FullyQualifiedName~RouteMap" -p:RouteMapOnly=true
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```
