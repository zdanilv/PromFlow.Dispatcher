# Описание работы ModbusDemo

## Назначение

`ModbusDemo` - отдельная вкладка для проверки сценария Modbus TCP на карте Holding Registers `16384..16419`.
Экран показывает телеметрию, отправляет командные биты и записывает изменяемые параметры через независимый demo-стек, который не разделяет runtime с основной вкладкой `Modbus TCP`.

## Точки входа

- UI вкладки: [WorkspaceView.axaml](Workspace/WorkspaceView.axaml) подключает [ModbusDemoView.axaml](Workspace/ModbusDemo/ModbusDemoView.axaml).
- Логика экрана: [ModbusDemoViewModel.cs](Workspace/ModbusDemo/ModbusDemoViewModel.cs).
- Code-behind поведения кнопок: [ModbusDemoView.axaml.cs](Workspace/ModbusDemo/ModbusDemoView.axaml.cs).
- Регистрация ViewModel и View: [Program.cs](../Configurator.Boot/Program.cs).
- Настройки demo-секции: [appsettings.json](../Configurator.Boot/appsettings.json), секция `ModbusDemo`.
- DI отдельного demo-стека: [DependencyInjection.cs](../Configurator.Infrastructure.Modbus/DependencyInjection.cs).
- Контракты demo-стека: [IModbusDemoTcpService.cs](../Configurator.Application/Services/Modbus/Contracts/IModbusDemoTcpService.cs), [IModbusDemoOptionsProvider.cs](../Configurator.Application/Services/Modbus/Contracts/IModbusDemoOptionsProvider.cs).
- Реализации: [ModbusDemoTcpService.cs](../Configurator.Infrastructure.Modbus/Runtime/ModbusDemoTcpService.cs), [ModbusDemoOptionsProvider.cs](../Configurator.Infrastructure.Modbus/Configuration/ModbusDemoOptionsProvider.cs).
- Регрессионные тесты: [ModbusDemoViewModelTests.cs](../Configurator.Infrastructure.Modbus.Tests/ModbusDemoViewModelTests.cs).

## Карта Holding Registers

В UI используются абсолютные адреса, а в `DataMap.Address` хранится смещение от `HoldingRegisterStartAddress = 16384`.

| UI address | DataMap address | Point name | Access | Назначение |
| --- | ---: | --- | --- | --- |
| `16384..16387` | `0..3` | `Telemetry_1..Telemetry_4` | `Read` | Телеметрия, разбор UInt16 на биты `I1..I16` |
| `16388..16391` | `4..7` | `Commands_1..Commands_4` | `Write` | Командные слова, сборка битов `Q1..Q16` в UInt16 |
| `16400..16419` | `16..35` | `MB_*` | `ReadWrite` | Числовые параметры `ushort` |

Клиент и сервер demo-секции настроены на `127.0.0.1:1502`, `UnitId = 1`, `HoldingRegistersEnabled = true`, `RegisterCount = 36`.

## Запуск и остановка

`Start Server` и `Start Client` в ViewModel запускают фоновые lifecycle-операции, чтобы UI не блокировался при подключении или ожидании TCP.
Перед запуском берется clone текущих настроек `ModbusDemo`; после изменения настроек они применяются при следующем старте.

Поток запуска:

1. `ModbusDemoView` вызывает `StartServerCommand` или `StartClientCommand`.
2. `ModbusDemoViewModel` собирает options и вызывает `IModbusDemoTcpService`.
3. `ModbusDemoTcpService` делегирует в отдельный `ModbusTcpService`.
4. `ModbusTcpService` валидирует `DataMap`, переключает роль runtime и запускает client или server.
5. `ModbusRuntimeService` поднимает `ModbusClientService` или `ModbusServerService`.

`Stop` отменяет активный lifecycle, затем вызывает `StopAsync`. При закрытии приложения [App.axaml.cs](App.axaml.cs) останавливает и основной Modbus runtime, и demo-фасад.

## Телеметрия

`TelemetryGroups` создаются в `CreateTelemetryGroups()`. Каждый `Telemetry_*` читается как UInt16 и разворачивается в строки `ModbusTelemetryBitRow`.

- `BitText` показывает `I1`, `I2`, ... по индексу младшего бита.
- `RawValueText` показывает исходное значение регистра.
- Красный индикатор включается только для настроенного бита, сейчас это `Telemetry_1` bit0.

Snapshot приходит из `ModbusTcpService.Subscribe(...)`; ViewModel переводит обновление на UI dispatcher и применяет значение к нужной группе.

## Команды

`CommandGroups` создаются в `CreateCommandGroups()`. Каждая строка меняет один бит, но запись в Modbus всегда идет целым UInt16 словом регистра.
Текущее локальное слово хранится в `_commandWords`, а `WriteCommandBitAsync()` меняет в нем только нужную маску.

Текущие типы контролов:

- `MomentaryButton` - пишет `1` на press и `0` на release. Используется для `Commands_1 Q3..Q6`, `Commands_4 Q1..Q2`.
- `CheckBox` - удерживает состояние. Используется для `Commands_1 Q2`.
- `ToggleButton` - удерживает состояние кнопкой. Используется для `Commands_3 Q1`.
- `RadioButtonToggle` - выглядит как radio, но удерживает состояние и снимается повторной активацией. Используется для `Commands_1 Q1`.

Для `RadioButtonToggle` в code-behind есть отдельное поведение: Avalonia `RadioButton` сам не снимает выбранное состояние при повторном клике, поэтому `ModbusDemoRadioButtonToggleBehavior` вручную переводит `IsChecked` в `false`.

## Параметры

`ParameterRows` создаются в `CreateParameterRows()` для `MB_*` регистров `16400..16419`.

- Polling обновляет только `LastReadValueText`.
- `ReadParametersCommand` копирует последнее прочитанное значение в поле редактирования.
- `WriteParametersCommand` валидирует все значения как `ushort` и записывает их по одному.
- Slider используется для `MB_ТЕКУЩАЯ_ПОЗИЦИЯ`; остальные параметры редактируются через `TextBox`.

Такой поток защищает ввод пользователя от перезаписи очередным polling snapshot.

## Настройки

Кнопка `Настройки` открывает общий диалог Modbus-настроек, но передает section name `ModbusDemo`.
Сохраненные настройки пишутся через `IAppConfigService.SaveSectionAsync(...)` и применяются при следующем запуске client/server.

`ModbusDemoOptionsProvider` всегда возвращает clone, чтобы ViewModel и диалог не меняли live-options напрямую.

## Проверка и troubleshooting

- Unit-тесты ModbusDemo находятся в [ModbusDemoViewModelTests.cs](../Configurator.Infrastructure.Modbus.Tests/ModbusDemoViewModelTests.cs).
- Быстрая проверка: `dotnet test Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore`.
- Проверка после изменения project metadata: `dotnet build DesktopTemplate.slnx --no-restore`.
- Если `Start Client` показывает ожидание подключения, проверьте, что demo-сервер или внешнее устройство слушает `127.0.0.1:1502`.
- Если запись команды не подтверждается, проверьте `Access` точки в `DataMap`, роль runtime и `WriteConfirmationTimeoutMs`.
- Если новый файл не виден в Visual Studio, проверьте явный `<None Update="Описание работы ModbusDemo.md" />` в [Configurator.Desktop.csproj](Configurator.Desktop.csproj).
