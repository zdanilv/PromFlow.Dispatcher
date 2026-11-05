# Обзор архитектуры

`PromFlow.Dispatcher` — Avalonia desktop-приложение с проектами Domain, Application,
Infrastructure, Desktop и Boot. Application ссылается на Domain; Desktop и
инфраструктурные проекты — на Application; Boot собирает их вместе.

| Слой | Фактическая ответственность |
|---|---|
| Domain | `Configurator.Domain` сейчас содержит файл проекта, но не содержит C# моделей. `SignalId` здесь — доменное имя сигнала, а не отдельный тип этого проекта. |
| Application | Контракты сигналов, Modbus и OPC UA, параметры и валидаторы без зависимости от Desktop и конкретного транспорта. |
| Infrastructure | Общие настройки и сервисы ОС — `Configurator.Infrastructure`; TCP client/server, общий runtime и фасады — `Configurator.Infrastructure.Modbus`; OPC UA SDK, client/server/browser/runtime — `Configurator.Infrastructure.OpcUa`. |
| Desktop | Avalonia/ReactiveUI Workspace, RouteMap definition, редакторы, ViewModels, UI-проекция состояния и журнал сессии. |
| Boot | `Program.cs` читает конфигурацию запуска и пользовательский overlay, регистрирует зависимости и запускает приложение. |

Для поиска конкретного владельца и теста используйте [карту кода](11-code-map.ru.md).

## Workspace

`Application.WorkMode` — режим запуска оболочки, а не рабочая настройка оборудования.
Он читается только из локального `Configurator.Boot/appsettings.json`: в `user` режиме
Windows-приложение не создаёт консоль, а в `admin` подключает родительскую либо создаёт
новую. На ALT Linux GUI-запуск admin перезапускается в `xterm`.
Изменяемые секции `RouteMapRuntime`, `Modbus`, `ModbusDemo`, `OpcUa`, `Help` и `Startup`
читаются поверх defaults из общего файла
`%LOCALAPPDATA%\Configurator\appsettings.json`, поэтому admin и user используют одну
и ту же конфигурацию маршрута, SignalId mapping, Modbus TCP, OPC UA, помощи и автозапуска.

`Help.Contacts` — список до десяти строк `{ Label, Value, Uri? }`. Если в общем
файле существует раздел `Help`, он целиком заменяет поставляемый список, поэтому
контакты не смешиваются по индексам JSON-массива. `Uri` разрешён только для `http`,
`https`, `mailto` и `tel`.

`Startup.Enabled` по умолчанию `true` и синхронизируется при старте: в Windows это
`HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run`, в ALT Linux —
`$XDG_CONFIG_HOME/autostart/promflow-dispatcher.desktop`. Linux desktop entry использует
`Terminal=true` только для admin-режима.

`Configurator.Boot/appsettings.json` содержит `Application.WorkMode`:

- `admin` — Workspace показывает вкладки `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог`, `Modbus TCP`;
- `user` — Workspace показывает только `Route Map` на всю рабочую область, без вкладок.

Неверное или отсутствующее значение трактуется как `admin`. Актуальный admin-режим
`WorkspaceView` содержит четыре вкладки:

```text
Route Map
SignalId ↔ Modbus
Менеджер тревог
Modbus TCP
```

`WorkspaceViewModel` получает через DI:

- `RouteMapDashboardViewModel` — операторская мнемосхема.
- `NotificationsPanelViewModel` — правая панель `Уведомления`/`История` RouteMap.
- `RouteMapSignalMappingViewModel` — редактор связей `SignalId ↔ Modbus`.
- `AlarmManagerViewModel` — редактор `Modbus.AlarmMap`.
- `ModbusDemoViewModel` — внутреннее имя модели экрана `Modbus TCP` для запуска, остановки и настройки общего TCP runtime.

`ModbusDemo.AutostartOnWorkspaceOpen` и `StartupMode` могут запускать общий runtime при
открытии Workspace.

В `user` режиме вкладки `SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus TCP` скрыты
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

Схема ниже относится к выбранному источнику `Modbus`;
`RouteMapSignalRuntime` может вместо него использовать `MockSignalProvider`.

```text
ModbusDemo endpoint/lifecycle
  -> shared ModbusRuntimeService
  -> RouteMap IModbusTcpService facade
  -> IModbusDataSnapshotSource
  -> ModbusTcpSignalValueProvider
  -> ISignalValueProvider
  -> RouteMapRuntimeMapper
  -> RouteMapDashboardViewModel
  -> Avalonia UI
```

RouteMap получает не coils/registers, а `SignalValue` по `SignalId`. Mapper переводит
значения, quality и stale-состояния в `RouteMapRuntimeState`.
`RouteMapDashboardViewModel` отдельно записывает snapshots в `RouteMapSessionJournal`
после перехода на UI thread; журнал питает панель истории.

## Поток записи

Это путь записи при источнике `Modbus`; при `Mock` переключаемый runtime использует
`MockEquipmentCommandDispatcher` без TCP-записи.

```text
TopBar / node menu / equipment card / card parameter dialog
  -> SignalWriteRequest
  -> IEquipmentCommandDispatcher
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService.SetAsync
  -> shared Modbus client or local server
  -> poll/readback
  -> RouteMapDashboardViewModel / Avalonia UI
```

UI может оптимистично обновить checked-состояние, но окончательная синхронизация приходит
через readback. Входной `ReadWrite` сигнал не должен порождать повторную запись.
После успешного `DispatchAsync` переключаемый `RouteMapSignalRuntime` отдельно записывает
отправленный сигнал в `RouteMapSessionJournal`.
Для карточки «Кюбель Л.К.» `ПУСК` отправляет только `StartCommand=true`, а
dispatcher завершает Pulse ограниченной попыткой `false`; `СТОП` отдельно пишет
Latched `StopCommand=true/false`. Команды не меняют бит друг друга. Runtime
readback и `StartOffFeedback`/`StopOffFeedback` обновляют UI без обратной записи в PLC.
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

## OPC UA: реализация и доступность

Контракты OPC UA находятся в `Configurator.Application/Services/OpcUa/`; клиент,
сервер, browser и runtime — в `Configurator.Infrastructure.OpcUa/`. Boot регистрирует
их через `AddOpcUaInfrastructure`. Desktop содержит типы `OpcUaViewModel` и `OpcUaView`,
а также диалоги тегов, но текущий `WorkspaceView.axaml` не содержит вкладку OPC UA:
пользовательский экран настройки OPC UA через Workspace сейчас недоступен.
`WorkspaceViewModel` не получает OPC UA runtime и не запускает его по настройке
`OpcUa.AutostartOnWorkspaceOpen`; регистрация сервисов в DI сама по себе не запускает роли.

`Configurator.Infrastructure.OpcUa.Tests` содержит проверки настроек, runtime,
валидации тегов и локальный интеграционный сценарий client/server с browse, read,
subscribe и write. Это покрытие тестами не подтверждает готовность подключения к
внешнему промышленному endpoint; такая проверка здесь не проводилась.

Текущая DI-регистрация использует `DisabledOpcUaSecurityProvider` и
`AnonymousOpcUaIdentityProvider`; defaults — `None`/`Anonymous`, сертификаты
отключены. Этот режим нельзя считать готовой защитой промышленного соединения.
Подробные контракты, ограничения lifecycle и безопасности — в
[руководстве OPC UA](10-opc-ua-guide.ru.md).

## Safety boundary

UI не является контуром функциональной безопасности. Interlock, аварийная логика,
разрешения исполнительных механизмов и окончательное принятие команд должны оставаться
в PLC.
`Application.WorkMode` управляет видимостью интерфейса, а не авторизацией команд PLC.
Доступ к настройкам ПК и права ОС нужно контролировать отдельно.

