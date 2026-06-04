# ModbusDemo: controls

`ModbusDemo` работает только с Holding Registers. В `appsettings.json` для секции `ModbusDemo` задана база `HoldingRegisterStartAddress = 16384`; в `DataMap.Address` хранится смещение от этой базы.

Примеры:

- HR `16384` -> `Address = 0`;
- HR `16388` -> `Address = 4`;
- HR `16400` -> `Address = 16`.

## Command controls

Команды задаются в `ModbusDemoViewModel.CreateCommandGroups()`. Один control меняет один бит, но запись в Modbus уходит целым `UInt16` словом.

```csharp
new("Commands_1", "C_ПУСК-ВРАЩЕНИЕ", 2, ModbusCommandControlKind.ToggleButton, WriteCommandBitAsync)
```

Актуальные типы:

- `RadioButtonToggle`: для `Commands_1 Q1`; выглядит как `RadioButton`, повторная активация снимает выбор и пишет `0`.
- `CheckBox`: для `Commands_1 Q2`; пишет held state.
- `ToggleButton`: для остальных command-кнопок, включая `Commands_1 Q3..Q6`, `Commands_3 Q1`, `Commands_4 Q1..Q2`.

Импульсных `Button`/`MomentaryButton` команд в `ModbusDemo` нет.

## Telemetry

Телеметрия описана в `CreateTelemetryGroups()`. Значение регистра разворачивается в биты от младшего к старшему: `I1 = bit0`, `I16 = bit15`.

Красный индикатор включается через `redIndicatorBitIndex`:

```csharp
new("Telemetry_1", 16384, ["Д_КЮБЕЛЬ_ОТКРЫТ"], redIndicatorBitIndex: 0)
```

## Parameters

Параметры описаны в `CreateParameterRows()`.

```csharp
CreateParameter("MB_Hz", 16404)
CreateParameter("MB_ТЕКУЩАЯ_ПОЗИЦИЯ", 16400, ModbusParameterEditorKind.Slider)
```

Редактирование не пишет Modbus сразу. Запись происходит только по команде `Записать значения`.
