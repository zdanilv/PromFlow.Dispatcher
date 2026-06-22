# ModbusDemo: добавление кнопок и реакций UI

`ModbusDemo` работает только с `HoldingRegister`. В конфигурации `ModbusDemo` задан
`HoldingRegisterStartAddress = 16384`, поэтому `DataMap.Address` хранит смещение от
этого адреса:

- регистр `16384` -> `Address = 0`;
- регистр `16388` -> `Address = 4`;
- регистр `16400` -> `Address = 16`.

Экран `Modbus Demo` владеет запуском, остановкой и настройкой общего Modbus TCP runtime.
Точки из этого документа добавляются только в `ModbusDemo.DataMap` и обслуживают demo UI.
RouteMap использует тот же TCP runtime, но отдельную карту `Modbus.DataMap`, которая
редактируется на вкладке `SignalId ↔ Modbus`. RouteMap schema v10 также добавляет
read-only fragment-сигналы линий вида `route.<segmentId>.fragment_<n>.active`; их не
нужно добавлять в `ModbusDemo.DataMap`.

## Новая точка DataMap

Добавьте точку в `Configurator.Boot/appsettings.json`, секция `ModbusDemo/DataMap`.
Не добавляйте сюда RouteMap SignalId: для них предназначена секция `Modbus/DataMap`.
Для одного 16-битного регистра используйте:

```json
{
  "Name": "My_Register",
  "Area": "HoldingRegister",
  "Address": 16,
  "Length": 1,
  "Access": "ReadWrite",
  "Type": "UInt16"
}
```

`Access` выбирайте по назначению: `Read` для телеметрии, `Write` для команд,
`ReadWrite` для изменяемых параметров.

## Командная кнопка

Команды описываются в `ModbusDemoViewModel.CreateCommandGroups()`.
Каждый бит получает имя точки регистра, подпись, номер бита и тип контрола:

```csharp
new("Commands_1", "C_ПУСК", 2, ModbusCommandControlKind.MomentaryButton, WriteCommandBitAsync)
```

Доступные типы:

- `MomentaryButton`: выполняет фиксированный импульс по клику: пишет `1`,
  удерживает командный бит 300 мс и пишет `0`.
- `RadioButtonPulse`: выглядит как `RadioButton`, но тоже работает импульсом `1 -> 0`.
- `CheckBox`: удерживает состояние, пишет `1` при включении и `0` при выключении.
- `ToggleButton`: удерживает состояние как переключатель.

Биты считаются от младшего: `Q1 = bit0`, `Q16 = bit15`.

## Реакция на телеметрию

Телеметрические биты описываются в `CreateTelemetryGroups()`.
Чтобы добавить красный индикатор для конкретного бита, передайте его индекс:

```csharp
new("Telemetry_1", 16384, ["Д_КЮБЕЛЬ_ОТКРЫТ"], redIndicatorBitIndex: 0)
```

UI сам показывает кружок, когда `IsRedIndicatorVisible = true`.
Биты считаются от младшего: `I1 = bit0`, `I16 = bit15`.

## Изменяемый параметр

Параметры описываются в `CreateParameterRows()`.
Обычное числовое поле:

```csharp
CreateParameter("MB_Hz", 16404)
```

Slider для диапазона `0..65535`:

```csharp
CreateParameter("MB_ТЕКУЩАЯ_ПОЗИЦИЯ", 16400, ModbusParameterEditorKind.Slider)
```

Параметры не отправляются при каждом изменении поля или Slider. Новые значения уходят
в Modbus TCP только по кнопке `Записать значения`.
