# Описание работы ModbusDemo

## Назначение

`ModbusDemo` - экран для проверки отдельного Modbus TCP стека по Holding Registers `16384..16419`. Экран показывает телеметрию, пишет командные биты целыми `UInt16` регистрами и позволяет редактировать параметры `MB_*`.

## Точки входа

- View: [ModbusDemoView.axaml](Workspace/ModbusDemo/ModbusDemoView.axaml)
- Q1 toggle-radio behavior: [ModbusDemoView.axaml.cs](Workspace/ModbusDemo/ModbusDemoView.axaml.cs)
- ViewModel и карта UI: [ModbusDemoViewModel.cs](Workspace/ModbusDemo/ModbusDemoViewModel.cs)
- Настройки `ModbusDemo`: [appsettings.json](../Configurator.Boot/appsettings.json)
- DI demo-стека: [DependencyInjection.cs](../Configurator.Infrastructure.Modbus/DependencyInjection.cs)
- Контракты: [IModbusDemoTcpService.cs](../Configurator.Application/Services/Modbus/Contracts/IModbusDemoTcpService.cs), [IModbusDemoOptionsProvider.cs](../Configurator.Application/Services/Modbus/Contracts/IModbusDemoOptionsProvider.cs)
- Реализации: [ModbusDemoTcpService.cs](../Configurator.Infrastructure.Modbus/Runtime/ModbusDemoTcpService.cs), [ModbusDemoOptionsProvider.cs](../Configurator.Infrastructure.Modbus/Configuration/ModbusDemoOptionsProvider.cs)
- Тесты: [ModbusDemoViewModelTests.cs](../Configurator.Infrastructure.Modbus.Tests/ModbusDemoViewModelTests.cs), [ModbusDemoViewUiTests.cs](../Configurator.Infrastructure.Modbus.Tests/UI/ModbusDemoViewUiTests.cs)

## Карта Holding Registers

`HoldingRegisterStartAddress = 16384`; `DataMap.Address` хранит смещение от этой базы.

| HR | DataMap.Address | Точка | Access | UI |
| --- | ---: | --- | --- | --- |
| `16384` | `0` | `Telemetry_1` | `Read` | битовые строки `I1..I16` |
| `16385` | `1` | `Telemetry_2` | `Read` | битовые строки |
| `16386` | `2` | `Telemetry_3` | `Read` | битовые строки |
| `16387` | `3` | `Telemetry_4` | `Read` | битовые строки |
| `16388` | `4` | `Commands_1` | `Write` | `Q1..Q6` |
| `16389` | `5` | `Commands_2` | `Write` | зарезервировано, строк нет |
| `16390` | `6` | `Commands_3` | `Write` | `Q1 СЕТЬ` |
| `16391` | `7` | `Commands_4` | `Write` | `Q1 ПУСК`, `Q2 СТОП` |
| `16400` | `16` | `MB_ТЕКУЩАЯ_ПОЗИЦИЯ` | `ReadWrite` | slider |
| `16401` | `17` | `MB_N_АВАРИЯ-ПОЗ_УПРАВ` | `ReadWrite` | textbox |
| `16402` | `18` | `MB_N_АВАРИЯ-ВРАЩЕНИЕ` | `ReadWrite` | textbox |
| `16403` | `19` | `MB_СТАТУС-ВРАЩЕНИЕ` | `ReadWrite` | textbox |
| `16404` | `20` | `MB_Hz` | `ReadWrite` | textbox |
| `16411` | `27` | `MB_ЦЕЛЬ_ПОЗИЦИЯ` | `ReadWrite` | textbox |
| `16412` | `28` | `MB_ВОЗВРАТ_ПОЗИЦИЯ` | `ReadWrite` | textbox |
| `16413` | `29` | `MB_ТОП_СБРОС-ПОЗ_УПРАВ` | `ReadWrite` | textbox |
| `16414` | `30` | `MB_ТОП_ФИЛЬТР-ПОЗ_УПРАВ` | `ReadWrite` | textbox |
| `16415` | `31` | `MB_ТОП_АВАРИЯ-ПОЗ_УПРАВ` | `ReadWrite` | textbox |
| `16416` | `32` | `MB_ТОП_ПАУЗА-ВРАЩЕНИЕ` | `ReadWrite` | textbox |
| `16417` | `33` | `MB_ТОП_АВАРИЯ-ВРАЩЕНИЕ` | `ReadWrite` | textbox |
| `16418` | `34` | `MB_ТОП_СБРОС-З_ВЫГРУЗКА` | `ReadWrite` | textbox |
| `16419` | `35` | `MB_ТОП_СБРОС-З_ЗАГРУЗКА` | `ReadWrite` | textbox |

## Запуск и остановка

`AddModbusInfrastructure()` регистрирует основной `Modbus` и named-секцию `ModbusDemo`. `CreateDemoTcpService()` вручную собирает отдельные `ModbusClientService`, `ModbusServerService`, `ModbusRuntimeService` и facade, чтобы демо-экран не делил состояние с основным Modbus TCP экраном.

`Start Client` и `Start Server` берут clone текущих настроек из ViewModel и запускают соответствующую роль через `IModbusDemoTcpService`. `Stop` отменяет активную lifecycle-операцию и вызывает `StopAsync()`.

## Телеметрия

ViewModel подписывается на readable-точки из `TelemetryGroups` и `ParameterRows`. Snapshot приходит с фонового polling-потока, поэтому `OnDataValueChanged()` возвращает обновление bound-свойств через UI dispatcher.

Для `Telemetry_*` значение `UInt16` разворачивается в биты. `Telemetry_1 Q1/I1` дополнительно управляет красным индикатором через `IsRedIndicatorVisible`.

## Команды

Каждая команда меняет один бит локального слова `_commandWords`, затем пишет целый Holding Register через `SetAsync(pointName, nextWord)`. При ошибке записи ViewModel откатывает локальное слово, чтобы следующий toggle не наследовал бит, который не ушел в устройство.

- `Commands_1 Q1 C_СБРОС` - `RadioButtonToggle`: выглядит как `RadioButton`, повторный клик снимает `IsChecked` и пишет `0`.
- `Commands_1 Q2 C_ОТМЕНА` - `CheckBox`.
- `Commands_1 Q3..Q6` - `ToggleButton`.
- `Commands_3 Q1 СЕТЬ` - `ToggleButton`.
- `Commands_4 Q1 ПУСК`, `Q2 СТОП` - `ToggleButton`.

Импульсных `press=1/release=0` команд в `ModbusDemo` нет.

## Параметры

Polling обновляет только `LastReadValueText`. `EditValueText` не затирается snapshot-ами, чтобы пользователь мог редактировать поле без гонки с чтением.

`Считать значения` копирует последний прочитанный snapshot в поля редактирования. `Записать значения` валидирует диапазон `0..65535` и последовательно пишет все `ParameterRows`.

## Настройки

Диалог настроек открывается для named-секции `ModbusDemo`. После сохранения ViewModel хранит clone настроек в `_currentOptions`; следующий `Start Client` или `Start Server` запускается уже с ним.

## Тесты и troubleshooting

Запуск:

```powershell
dotnet test Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj
dotnet build DesktopTemplate.slnx --no-restore
```

UI-тесты headless проверяют реальные клики по `RadioButton`, `ToggleButton`, кнопку записи параметров и видимое обновление телеметрии.

Типовые проверки:

- Q1 не снимается повторным кликом: смотреть tunnel handlers в [ModbusDemoView.axaml.cs](Workspace/ModbusDemo/ModbusDemoView.axaml.cs) и тест `RadioButtonQ1_ClickTwice_WritesOneThenZero`.
- Команда пишет неверный бит: проверить `BitIndex`, `_commandWords` и `WriteCommandBitAsync()` в [ModbusDemoViewModel.cs](Workspace/ModbusDemo/ModbusDemoViewModel.cs).
- Параметр не обновляется: имя точки в `CreateParameterRows()` должно совпадать с `ModbusDemo/DataMap`.
- Настройки не применяются: проверить `ModbusOptions.DemoSectionName` и сохранение секции `ModbusDemo`.
