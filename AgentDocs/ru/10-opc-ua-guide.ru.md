# OPC UA: устройство и границы реализации

Руководство описывает код ветки `17-demo-v2`. Для изменения поведения проверяйте указанные исходники и тесты: наличие типа, настройки или тестового сценария само по себе не означает доступность функции оператору.

## Назначение и фактический статус

OPC UA представлен отдельными контрактами, клиентом, встроенным сервером, сервисом просмотра адресного пространства и runtime для двух ролей. Клиент умеет открывать сессию, читать и записывать скалярные значения, создавать подписки. Сервер публикует telemetry-переменные и принимает запись в command-переменные. Эти возможности подтверждены реализацией и локальным интеграционным тестом; подключение к промышленному endpoint не проверено. Основные точки входа: [`IOpcUaRuntimeService`](../../Configurator.Application/Services/OpcUa/Contracts/IOpcUaRuntimeService.cs), [`OpcUaRuntimeService`](../../Configurator.Infrastructure.OpcUa/Runtime/OpcUaRuntimeService.cs), [`OpcUaIntegrationTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaIntegrationTests.cs).

**Доступность в приложении ограничена.** [`Program.cs`](../../Configurator.Boot/Program.cs) вызывает `AddOpcUaInfrastructure`, но [`WorkspaceView.axaml`](../../Configurator.Desktop/Workspace/WorkspaceView.axaml) содержит только `Route Map`, `SignalId ↔ Modbus`, `Менеджер тревог` и `Modbus TCP`. [`WorkspaceViewModel.cs`](../../Configurator.Desktop/Workspace/WorkspaceViewModel.cs) не получает OPC UA runtime или `OpcUaViewModel`. Файлы экрана и диалогов есть, однако входа в них из текущего Workspace нет. Настройки `OpcUa.AutostartOnWorkspaceOpen` и `StartupMode` существуют, но текущий Workspace их не использует для запуска OPC UA.

## Архитектура и владельцы изменений

| Слой | Ответственность и место изменения |
|---|---|
| Application | SDK-независимые [`Contracts`](../../Configurator.Application/Services/OpcUa/Contracts/), [`OpcUaOptions`](../../Configurator.Application/Services/OpcUa/Configuration/OpcUaOptions.cs), [`OpcUaNodeOptions`](../../Configurator.Application/Services/OpcUa/Configuration/OpcUaNodeOptions.cs), модели адресов/значений, результаты операций, валидаторы типов и списков тегов. |
| Infrastructure.OpcUa | [`OpcUaClientService`](../../Configurator.Infrastructure.OpcUa/Client/OpcUaClientService.cs), [`OpcUaTagBrowserService`](../../Configurator.Infrastructure.OpcUa/Client/OpcUaTagBrowserService.cs), [`OpcUaServerService`](../../Configurator.Infrastructure.OpcUa/Server/OpcUaServerService.cs), [`OpcUaNodeManager`](../../Configurator.Infrastructure.OpcUa/Server/OpcUaNodeManager.cs), [`OpcUaRuntimeService`](../../Configurator.Infrastructure.OpcUa/Runtime/OpcUaRuntimeService.cs) и настройка SDK. |
| Desktop | [`OpcUaViewModel`](../../Configurator.Desktop/Workspace/OpcUa/OpcUaViewModel.cs), [`OpcUaView`](../../Configurator.Desktop/Workspace/OpcUa/OpcUaView.axaml), строки тегов и диалоги редактирования/импорта. Это существующие, но не включённые в Workspace компоненты. |
| Boot | [`Program.cs`](../../Configurator.Boot/Program.cs) регистрирует инфраструктуру и диалоги; [`appsettings.json`](../../Configurator.Boot/appsettings.json) содержит поставляемые значения секции `OpcUa`. |

Слой Desktop использует Application-контракты; вызовы OPC UA SDK сосредоточены в Infrastructure. Это проверяется также [`CleanArchitectureTests`](../../Configurator.Infrastructure.OpcUa.Tests/CleanArchitectureTests.cs).

## Конфигурация и Dependency Injection

[`DependencyInjection.AddOpcUaInfrastructure`](../../Configurator.Infrastructure.OpcUa/DependencyInjection.cs) связывает `OpcUaOptions` с секцией `OpcUa`, регистрирует валидаторы, client/server/browser/runtime как singleton. Один `OpcUaClientService` реализует `IOpcUaClientService`, `IOpcUaTagWriter`, `IOpcUaConnectionStateProvider`; один `OpcUaServerService` реализует `IOpcUaServerService` и `IOpcUaServerTagUpdater`. `OpcUaServerCommandState` передаёт входящие команды runtime.

В поставляемом [`appsettings.json`](../../Configurator.Boot/appsettings.json) заданы `AutostartOnWorkspaceOpen=false`, `StartupMode=None`, `opc.tcp://localhost:4840` для клиента и сервера, `Security.Mode=None`, `Authentication.Mode=Anonymous`, `Certificates.Enabled=false`. Модель `OpcUaOptions` хранит также `KnownEndpoints`, таймауты, интервалы подписок и demo-telemetry. [`OpcUaViewModel.BuildOptions`](../../Configurator.Desktop/Workspace/OpcUa/OpcUaViewModel.cs) формирует параметры экранного сценария; `SaveOpcUaOptions` сохраняет их через `IAppConfigService` в пользовательских настройках, если этот ViewModel создан. Это не свидетельство запуска runtime в обычном Workspace.

`OpcUaNodeOptions.UseConfiguredTags=false` по умолчанию выбирает встроенные demo-теги. При `true` методы `ServerTelemetryDefinitions`, `ClientTelemetryDefinitions`, `ServerCommandDefinitions`, `ClientCommandDefinitions` используют соответствующие списки и предусмотренные в модели fallback-наборы. Перед изменением адресного пространства проверяйте эти методы и [`OpcUaOptionsTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaOptionsTests.cs).

## Клиент: сессия, чтение, запись, подписки

[`OpcUaClientService.ConnectAsync`](../../Configurator.Infrastructure.OpcUa/Client/OpcUaClientService.cs) проверяет режим `None`/`Anonymous` и отключённые сертификаты, создаёт явно заданный endpoint и SDK-сессию с `CancellationToken`. Успех/ошибка публикуются через `IOpcUaConnectionStateProvider`. `ReadTagAsync` разрешает `NamespaceUri` и `Identifier` в `NodeId`, читает `Value` и возвращает значение, время и строку OPC UA status. `WriteTagAsync` проверяет запрос через `OpcUaTagWriteRequestValidator`, вызывает SDK `WriteAsync` и проверяет код ответа сервера. Разрешение записи конкретного удалённого узла определяет сервер, а не один только клиентский валидатор.

`SubscribeAsync` создаёт `Subscription` и `MonitoredItem` для `Attributes.Value`; callback передаёт `OpcUaTagValue`. [`OpcUaRuntimeService.SubscribeClientTelemetryAsync`](../../Configurator.Infrastructure.OpcUa/Runtime/OpcUaRuntimeService.cs) сначала читает настроенные client telemetry-теги, затем подписывается на изменения. Ошибка чтения или создания подписки попадает в snapshot как значение с ошибкой и в `LastError`; успешное подключение не гарантирует, что все теги доступны.

[`OpcUaTagBrowserService`](../../Configurator.Infrastructure.OpcUa/Client/OpcUaTagBrowserService.cs) использует отдельную временную сессию для `BrowseAsync`; обход начинается с `ObjectsFolder` и ограничен `MaxDepth`/`MaxNodes`. Он читает метаданные узлов и помечает неподдерживаемые или не подходящие по доступу теги. `DiscoverEndpointsAsync` объединяет известные адреса и результаты discovery; найденные endpoint-ы фильтруются по `None`, а не становятся автоматически выбранным защищённым соединением. Закрытие временной сессии выполняется в `finally`.

## Сервер: адресное пространство и входящие команды

[`OpcUaServerService.StartAsync`](../../Configurator.Infrastructure.OpcUa/Server/OpcUaServerService.cs) запускает SDK-сервер по заданному endpoint; при обновлении тега сервис ждёт создания `OpcUaNodeManager`. `CreateAddressSpace` строит объект устройства с папками `Telemetry` и `Commands`: telemetry-переменные имеют `CurrentRead`, command-переменные — `CurrentReadOrWrite`. `OnWriteCommandValue` проверяет CLR-тип по типу узла; успешная внешняя запись обновляет значение и отправляется через [`OpcUaServerCommandState`](../../Configurator.Infrastructure.OpcUa/Server/OpcUaServerCommandState.cs) в server snapshot runtime. Код состояния хранит последнюю команду и журналирует её значение; это следует учитывать при выборе данных для будущей интеграции.

`IOpcUaServerTagUpdater.UpdateTagValueAsync` обновляет значение существующего серверного узла через `OpcUaNodeManager`; runtime использует это для начальных telemetry-значений и опционального demo-цикла `PeriodicTimer`. В этом пути нет привязки OPC UA command-тега к PLC-команде или промышленного interlock: не приписывайте приёму command-значения исполнительный эффект.

## Типы, направления и состояние данных

[`OpcUaDataTypeSupport`](../../Configurator.Application/Services/OpcUa/Common/OpcUaDataTypeSupport.cs) поддерживает скалярные `Boolean`, целые знаковые/беззнаковые типы от 8 до 64 бит, `Float`, `Double`, `String`. [`OpcUaTagConfigurationValidator`](../../Configurator.Application/Services/OpcUa/Validation/OpcUaTagConfigurationValidator.cs) проверяет адрес, имя, тип и дубли внутри списков; [`OpcUaTagWriteRequestValidator`](../../Configurator.Application/Services/OpcUa/Validation/OpcUaTagWriteRequestValidator.cs) проверяет адрес, ненулевое значение и допустимый CLR-тип перед записью. Browser отклоняет массивы/структуры и проверяет read/write access для цели импорта. Сервер создаёт telemetry read-only и command read/write; точная проверка типа внешней записи находится в `OpcUaNodeManager.OnWriteCommandValue`.

[`OpcUaRuntimeService`](../../Configurator.Infrastructure.OpcUa/Runtime/OpcUaRuntimeService.cs) хранит раздельные `ClientSnapshot` и `ServerSnapshot` со списками telemetry/command значений, публикует `StatusChanged` и `SnapshotChanged`. `OpcUaStatus` содержит состояния обеих ролей, сообщения и `LastError`. События runtime могут прийти с фонового потока; [`OpcUaViewModel.OnRuntimeStatusChanged` и `OnRuntimeSnapshotChanged`](../../Configurator.Desktop/Workspace/OpcUa/OpcUaViewModel.cs) ставят обновление коллекций на `Dispatcher.UIThread`, когда этот экран используется.

`ReadTagAsync` возвращает успешный `OpcUaOperationResult` с фактическим `StatusCode` даже для плохого кода качества; он не превращает такой код в ошибку операции. `OpcUaTagRow.SetFromValue` отдельно отмечает строки со статусом `Bad*` или `Error*`. Потребители API должны оценивать и результат вызова, и статус значения.

## Lifecycle, отмена и reconnect

`OpcUaRuntimeService` сериализует команды start/stop/restart через `SemaphoreSlim`, может запускать роли по отдельности или вместе и при общей остановке отключает клиент перед сервером. Клиент защищает ссылку на сессию и список подписок блокировкой; сервер сериализует start/stop отдельным gate. `StopClientCoreAsync` освобождает дескрипторы подписок и вызывает `DisconnectAsync`; тот закрывает SDK-сессию. `DisposeAsync` runtime отключает наблюдателей и останавливает роли; `OpcUaViewModel.Dispose` отписывает свои обработчики событий. Demo-цикл имеет `CancellationTokenSource` и ограниченное ожидание при остановке.

**Reconnect реализован частично.** Клиент подписан на SDK `KeepAlive`: плохой статус публикует `Reconnecting`, а `TransferSubscriptionsOnReconnect=true` выставлен для сессии. В `OpcUaClientService` и `OpcUaRuntimeService` нет собственного цикла повторного `ConnectAsync` по `ReconnectPeriodMilliseconds` и нет проверки повторного восстановления подписок в профильных тестах. Поэтому параметр интервала и состояние `Reconnecting` нельзя трактовать как гарантию автоматического восстановления. `CommandWriteIntervalMilliseconds` присутствует в options, но runtime пишет client command-тег по явному вызову `WriteClientTagAsync`; периодическая отправка команд кодом не подтверждена.

Есть отдельное ограничение дескриптора `SubscribeAsync`: его `Dispose()` удаляет объект из локального списка `OpcUaClientService`, но сам SDK `Subscription` в этом callback не удаляется из активной сессии. При `DisconnectAsync` сессия закрывается. До исправления и отдельного теста не рассчитывайте на немедленную отмену сетевого monitored item при индивидуальном `Dispose()`.

## Безопасность и доступ

DI использует [`DisabledOpcUaSecurityProvider`](../../Configurator.Application/Services/OpcUa/Security/DisabledOpcUaSecurityProvider.cs) и [`AnonymousOpcUaIdentityProvider`](../../Configurator.Application/Services/OpcUa/Security/AnonymousOpcUaIdentityProvider.cs). Клиент и browser отклоняют другие security/identity-режимы; сервер отклоняет security mode, отличный от `None`, и `Certificates.Enabled=true`. [`OpcUaApplicationConfigurationFactory`](../../Configurator.Infrastructure.OpcUa/Runtime/OpcUaApplicationConfigurationFactory.cs) создаёт SDK-конфигурацию с `SecurityPolicies.None`; для сервера он также создаёт временный self-signed сертификат в памяти. Наличие этого SDK-сертификата не включает защищённый endpoint, постоянное доверенное хранилище или проверенную аутентификацию. `AutoAcceptUntrustedCertificates=false`; развёртывание с производственной защитой не реализовано и не подтверждено.

OPC UA command-теги сервера доступны для записи через текущий `None`/`Anonymous` endpoint. Сетевой доступ и авторизацию нужно ограничивать на уровне среды и сервера до любой эксплуатации; UI и `Application.WorkMode` не являются промышленным interlock. Не связывайте эти теги с реальными исполнительными механизмами без отдельного проекта безопасности, требований к authentication/certificates и протокольных тестов. Никогда не переводите защищённый endpoint в `None` ради подключения.

## Безопасная проверка и диагностика

Для воспроизведения используйте mocks или локальный `opc.tcp://localhost:<свободный порт>` из `OpcUaIntegrationTests`; этот тест поднимает встроенный сервер, выполняет browse/read/subscribe/write demo-тегов и останавливает обе роли в `finally`. Не направляйте тесты, discovery или команды записи на внешний промышленный endpoint без явного разрешения и установленной среды. Для ошибки смотрите `OpcUaOperationResult.Error`, `OpcUaStatus.LastError`, статус конкретного `OpcUaTagValue`, затем журнал клиента/сервера. При `Connected` с ошибкой тега сначала проверяйте `NamespaceUri`, `Identifier`, тип, access level и список `Nodes.*Tags`; при `Reconnecting` не предполагается автоматическое восстановление.

Фокусная команда из корня репозитория (только для изолированной loopback-среды):

```powershell
dotnet test .\Configurator.Infrastructure.OpcUa.Tests\Configurator.Infrastructure.OpcUa.Tests.csproj --no-restore
```

Команда приведена как маршрут проверки; её выполнение и результат должны фиксироваться отдельно. Тесты не доказывают работу с внешним контроллером или защищённым endpoint.

## Соответствие функций и тестов

| Функция | Основная реализация | Проверка и граница покрытия |
|---|---|
| Defaults, JSON binding, списки тегов | `OpcUaOptions`, `OpcUaNodeOptions` | [`OpcUaOptionsTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaOptionsTests.cs): defaults, binding, clone, legacy/demo-списки. |
| Управление ролями, snapshots, ошибки тегов | `OpcUaRuntimeService` | [`OpcUaRuntimeTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaRuntimeTests.cs): режимы, независимость ролей, публикация значений и ошибок; в основном mock-сервисы. |
| Адрес/тип/дубли и запрос записи | `OpcUaTagConfigurationValidator`, `OpcUaTagWriteRequestValidator` | [`OpcUaTagWriteRequestValidatorTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaTagWriteRequestValidatorTests.cs): допустимые скаляры, ошибки адреса/типа, дубли. |
| Client/server browse, read, subscribe, write | `OpcUaClientService`, `OpcUaTagBrowserService`, `OpcUaServerService`, `OpcUaNodeManager` | [`OpcUaIntegrationTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaIntegrationTests.cs): один сценарий на loopback с demo-тегами; не покрывает защищённое соединение и внешнее оборудование. |
| Сценарии отдельного OPC UA ViewModel | `OpcUaViewModel`, диалог импорта | [`OpcUaViewModelTests`](../../Configurator.Infrastructure.OpcUa.Tests/OpcUaViewModelTests.cs): добавление/импорт тегов и отмена изменения; не доказывает наличие вкладки в Workspace. |
| Разделение SDK и слоёв | Application, Desktop, Infrastructure | [`CleanArchitectureTests`](../../Configurator.Infrastructure.OpcUa.Tests/CleanArchitectureTests.cs): ссылки проектов/SDK, без runtime-проверки. |

## Ограничения и дальнейшее сопровождение

| Статус | Факт и следующая точка проверки |
|---|---|
| Реализовано в сервисах | Локальный client/server сценарий с чтением, подпиской, записью и browse. При изменении протокола расширяйте `OpcUaIntegrationTests` на loopback. |
| Частично | `KeepAlive` сообщает `Reconnecting`, но прикладной цикл reconnect и его тест отсутствуют. Проверяйте `OpcUaClientService` и `OpcUaRuntimeService` перед обещанием восстановления. |
| Частично | Индивидуальный `Dispose()` подписки не удаляет SDK-подписку из сессии. Нужны исправление lifecycle и регрессионный тест до опоры на такую отмену. |
| Не подключено к Workspace | `OpcUaView`/`OpcUaViewModel` существуют, но текущий `WorkspaceView.axaml` не открывает их; автозапуск OPC UA в Workspace не вызывается. Любое подключение экрана потребует отдельной задачи и проверки режима `admin`/`user`. |
| Не реализовано | Защищённые политики, аутентификация кроме Anonymous и управляемое доверие сертификатам. Начинать с требований безопасности и тестов, а не с включения одного флага. |
| Не подтверждено | Интеграция с PLC, production endpoint, гарантированная отмена всех операций SDK и поведение после сетевого разрыва. Проверять на разрешённом стенде; не переносить safety-логику из PLC в UI. |

При изменениях держите Application-контракты независимыми от SDK, сверяйте статус функции с реальным входом в Workspace и расширяйте профильный тест для конкретного состояния сессии, типа тега или security-режима. Сохраняйте запрет на автоматический повтор потенциально неидемпотентной команды.
