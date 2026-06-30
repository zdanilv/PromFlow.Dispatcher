# Обзор архитектуры

`PromFlow.Dispatcher` — Avalonia desktop-приложение с разделением на Boot, Desktop,
Application и Infrastructure. RouteMap находится в desktop-слое, но transport/runtime
интеграция вынесена в application contracts и Modbus infrastructure.

## Workspace

`Application.WorkMode` — режим запуска оболочки, а не рабочая настройка оборудования.
Он читается из локального `Configurator.Boot/appsettings.json`. Изменяемые секции
`RouteMapRuntime`, `Modbus` и `ModbusDemo` читаются поверх defaults из общего файла
`%LOCALAPPDATA%\Configurator\appsettings.json`, поэтому admin и user используют одну
и ту же конфигурацию маршрута, SignalId mapping и Modbus TCP.

`Configurator.Boot/appsettings.json` содержит `Application.WorkMode`:

- `admin` — Workspace показывает вкладки `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог`, `Modbus Demo`;
- `user` — Workspace показывает только `Route Map` на всю рабочую область, без вкладок.

Неверное или отсутствующее значение трактуется как `admin`. Актуальный admin-режим
`WorkspaceView` содержит четыре вкладки:

```text
Route Map
SignalId ↔ Modbus
Менеджер тревог
Modbus Demo
```

`WorkspaceViewModel` получает через DI:

- `RouteMapDashboardViewModel` — операторская мнемосхема.
- `NotificationsPanelViewModel` — правая панель `Уведомления`/`История` RouteMap.
- `RouteMapSignalMappingViewModel` — редактор связей `SignalId ↔ Modbus`.
- `AlarmManagerViewModel` — редактор `Modbus.AlarmMap`.
- `ModbusDemoViewModel` — экран запуска, остановки и настройки общего TCP runtime.

`ModbusDemo.AutostartOnWorkspaceOpen` и `StartupMode` могут запускать общий runtime при
открытии Workspace.

В `user` режиме вкладки `SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus Demo` скрыты
визуально. Вкладка `Менеджер тревог` доступна только admin, но `ModbusAlarmMonitor`
запускается в обоих режимах Workspace, читает сохраненный `Modbus.AlarmMap` и показывает
диалоги по фронту alarm-бита.
Запись AlarmMap содержит `Enabled`, `Id`, `Kind`, `Message`, входной `Alarm`-бит,
отдельный `Acknowledgement`-бит, `RepeatIntervalMs` и
`AcknowledgementPulseDurationMs`; UI показывает их как колонки `Вкл.`, `Тип`,
`Сообщение`, `Alarm area/Offset/Bit`, `OK area/Offset/Bit`, `Repeat ms` и `Pulse ms`.
`Kind` принимает `Fault`, `Confirmation` или `Message`; это влияет только на визуальный
стиль диалога, а alarm/ack/repeat поведение остается общим.
После показа диалога тревога появляется во вкладке `Уведомления` правой панели RouteMap.
`Хорошо` снимает маркер непрочитанного; кнопка `X` удаляет элемент только после
`Alarm=false`, а `Очистить список` массово удаляет только такие же закрываемые элементы.
Вкладка `История` хранит только in-memory события текущей сессии: успешно отправленные
`SignalId`, первые/измененные полученные значения из `Modbus.DataMap` и события тревог
из `Modbus.AlarmMap`. История расширяет правую панель по ширине таблицы, показывает
направление стрелками и выводит адрес как числовой offset без названия Modbus area.

## Поток чтения

```text
ModbusDemo endpoint/lifecycle
  -> shared ModbusRuntimeService
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> RouteMapSessionJournal
  -> Avalonia UI
```

RouteMap получает не coils/registers, а `SignalValue` по `SignalId`. Mapper переводит
значения, quality и stale-состояния в `RouteMapRuntimeState`.

## Поток записи

```text
TopBar / node menu / equipment card / card parameter dialog
  -> SignalWriteRequest
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService.SetAsync
  -> shared Modbus client or local server
  -> poll/readback
  -> RouteMapSessionJournal
```

UI может оптимистично обновить checked-состояние, но окончательная синхронизация приходит
через readback. Входной `ReadWrite` сигнал не должен порождать повторную запись.
Карточные `ПУСК` и `СТОП` взаимоисключающие: включение одной кнопки сначала пишет
`false` в команду другой, затем `true` в выбранную команду. Runtime readback и
`StartOffFeedback`/`StopOffFeedback` обновляют UI без обратной записи в PLC.
Диалог параметров карточки, открываемый кнопкой `Н`, использует тот же
`IEquipmentCommandDispatcher`: строки `Write` и `ReadWrite` отправляются как
`SignalWriteRequest`, а `Read` строки только показывают последнее хорошее значение.

## DI и владельцы состояния

- `Configurator.Boot/Program.cs` собирает desktop DI и переключаемый `IRouteMapSignalRuntime`.
- `Configurator.Infrastructure.Modbus/DependencyInjection.cs` регистрирует общий runtime,
  RouteMap facade и demo facade.
- `RouteMapConfigurationManager` — единственный владелец `CurrentDocument` и
  `CurrentDefinition`.
- `RouteMapSessionJournal` — общий in-memory журнал уведомлений и истории сессии;
  `NoopSessionJournalExporter` вызывается при закрытии приложения как будущая точка
  выгрузки в БД, но БД сейчас не реализована.
- `EquipmentCardParametersDialogService` — desktop-сервис модального диалога параметров
  карточки; описание параметров хранится в RouteMap definition, а PLC-адреса остаются в
  `Modbus.DataMap`.
- `RouteMapConfigurationStorage`, `Migrator`, `Validator` и `Mapper` обслуживают
  загрузку, миграцию, проверку и преобразование JSON-профиля.
- `MockSignalProvider` и `ModbusTcpSignalValueProvider` читают актуальную definition через manager.

## Modbus runtime

`ModbusRuntimeService` координирует client/server роли. `ModbusDemo` управляет endpoint,
port, UnitId, start addresses, autostart и lifecycle. RouteMap использует тот же runtime,
но отдельную карту данных.

```text
ModbusDemo.DataMap -> demo UI facade
Modbus.DataMap     -> RouteMap facade and SignalId mapping tab
Modbus.AlarmMap    -> alarm dialogs in admin/user and acknowledgement pulses
```

`system.fault` находится в `Modbus.DataMap` как PLC-mapped системный SignalId общей
аварии RouteMap. Операторские диалоги при этом остаются только в `Modbus.AlarmMap`.

Не переносите endpoint/lifecycle в RouteMap. RouteMap отвечает за доменные bindings и
интерпретацию сигналов, а не за физическое подключение PLC.

## Safety boundary

UI не является контуром функциональной безопасности. Interlock, аварийная логика,
разрешения исполнительных механизмов и окончательное принятие команд должны оставаться
в PLC.

