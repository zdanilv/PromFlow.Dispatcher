# PromFlow.Dispatcher — результат этапов 3 и 4

Дата: 2026-11-19. Ветка: `feature/equipment-settings-add`. Исходный HEAD: `f1c95dc90a537482d224e240a516ca90b6476831`.
Аудит: `d378a3077d77dd5b00e133b80a1a28f6920bd983`. Реализованы только P1-04 и P1-03.
Текст запроса взят из пользовательского вложения; план и аудит использованы как требования и исходные доказательства.

## 1. P1-04: независимое качество областей

`ModbusAreaReadState` хранит Enabled, наличие успешного чтения, результат последней попытки,
время последнего успеха и идентификатор успешного чтения отдельно для Coils и HoldingRegisters.
Источник дополнительно идентифицируется ролью, SourceSessionId и порядком начала acquisition.
Маркер чтения назначается до I/O под существующим gate, включая чтения неизменившихся значений.
Ошибка сохраняет последнюю успешную метку области, но делает область недостоверной.
Перенесённый массив не декодируется как новый достоверный readback.

Фасад исключает недостоверные значения из текущего DataSnapshot и кеша GetAsync, очищает
непригодный register shadow. Register-bit RMW после failed HR read возвращает
`ModbusRegisterShadowUnavailable` до отправки. Серверный snapshot в Both не используется как
основание клиентского RMW. Провайдер SignalValue вычисляет timestamp/Good/Stale по исходной области.
Частичный отказ сохраняет Running и прежнюю диагностику; здоровая область остаётся Good,
`connection.connected` сохраняет действующий общий контракт.

Порядок публикаций защищён дополнительным `PublicationSequence`: поздний callback прежней карты
не восстанавливает Good после remap. Повторная проверка принятого raw snapshot защищает commits
при конкурентном runtime producer. Обычная смена текста статуса не повышает sequence данных.

P1-04 воспроизведён на HEAD-исходниках Modbus в изолированном `obj/stability-baseline`:
после трёх FC1 exceptions в том же TCP-сеансе FC3 продолжал успешно отвечать, старый Coil
оставался в Values с новым timestamp. Это доказательство исходного дефекта, а не PASS исправления.
Постоянные тесты теперь проверяют исправленное поведение на управляемом loopback peer.

## 2. P1-03: барьер readback

Контекст операции фиксирует полную копию DataMap-точки, роль, endpoint/параметры,
идентичность runtime options, поколения карты и source session, предыдущий успешный маркер
области и timeout. Внутренний `IModbusObservedWriter` возвращает receipt с ролью,
сеансом и порядковым маркером фактической отправки. Маркер назначается под тем же I/O gate,
что чтения, непосредственно перед транспортным вызовом или изменением server datastore.

Confirmation использует отдельное наблюдение каждого цикла нужной роли, включая равные значения.
Успех требует совпадения контекста и значения, Good нужной области и raw/area markers строго
после receipt. Старый snapshot, обработанный после отправки, либо прочитанный во время ожидания
write gate, не подтверждает запись. Смена роли/endpoint/карты/session инвалидирует ожидание
с `ModbusWriteConfirmationContextChanged`; отсутствие нового readback даёт прежний timeout.
Session A → B → A между проверками также инвалидирует операцию. Deadline использует Stopwatch.

Подписчики точек остаются change-only. Синхронный post-write read, пришедший до возврата
транспортного метода, пригоден для подтверждения. `WriteConfirmationTimeoutMs=0`, write-only
и rising edge короткого Pulse сохраняют отсутствие обязательного readback. Для отпускания
Pulse требуется новый достоверный false там, где включён существующий confirmation-контракт.
Новых отправок или повторов не добавлено.

P1-03 сначала воспроизведён постоянным тестом
`Confirmation_PreWriteEqualValueWithoutNewReadTimesOut`: до исправления actual Success вместо
ожидаемого timeout (FAIL assertion); после исправления PASS.

## 3. Изменённые файлы

| Файл | Назначение |
|---|---|
| [ModbusSnapshot.cs](../../Configurator.Application/Services/Modbus/Runtime/ModbusSnapshot.cs) | Состояния областей, acquisition/session identity; legacy decode fallback с нулевым маркером |
| [ModbusDataSnapshot.cs](../../Configurator.Application/Services/Modbus/Runtime/ModbusDataSnapshot.cs) | Передача source/area metadata и порядка публикации |
| [ModbusClientService.cs](../../Configurator.Infrastructure.Modbus/Client/ModbusClientService.cs) | Независимые результаты polling, markers под I/O gate, write receipt, очистка сеанса |
| [ModbusServerService.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusServerService.cs) | Метаданные server snapshots и упорядоченный write receipt |
| [ModbusTcpService.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs) | Декодирование по области, shadow, поколения, readback context/barrier, порядок commits |
| [ModbusTcpSignalValueProvider.cs](../../Configurator.Infrastructure.Modbus/RouteMap/ModbusTcpSignalValueProvider.cs) | Point-specific качество/время и отбрасывание задержанных публикаций |
| [ModbusObservationSequence.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusObservationSequence.cs) | Общий process-local монотонный порядок |
| [IModbusObservedWriter.cs](../../Configurator.Infrastructure.Modbus/Runtime/IModbusObservedWriter.cs) | Внутренний транспортный receipt без изменения публичных write API |
| [ModbusTcpServiceTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusTcpServiceTests.cs) | Общая partial fixture и управляемая публикация/смена endpoint в double |
| [ModbusAreaFreshnessTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusAreaFreshnessTests.cs) | Same-session loopback: частичные отказы, восстановление, SignalValue, RMW |
| [ModbusSourceQualityTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusSourceQualityTests.cs) | Роли, remap, Both и задержанные callbacks/publications |
| [ModbusWriteConfirmationTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusWriteConfirmationTests.cs) | Управляемые write/readback регрессии |
| Этот отчёт | Матрица и фактические проверки |

Пользовательские изменения `.codex/*` и ранее существовавший untracked аудит сохранены;
они не относятся к реализации. Временный baseline runner находится в игнорируемом `obj`.

## 4. Матрица A1–A10

Во всех строках фактическое поведение совпало с ожидаемым; статус PASS.
Имена ниже относятся к постоянным тестам текущего дерева.

| ID | Ожидаемое и фактическое поведение | Тест / доказательство | Статус |
|---|---|---|---|
| A1 | Обе успешно прочитанные области достоверны | `Client_TracksSuccessfulUnchangedCyclesIndependentlyForBothAreas` | PASS |
| A2 | Повторные FC1 failures не освежают Coils; Registers Good | `Client_PartialAreaFailurePreservesLastSuccessAndRecoveryNeedsNewRead(true)` | PASS |
| A3 | Повторные FC3 failures не освежают Registers; Coils Good | Тот же тест `(false)` | PASS |
| A4 | Первый отказ не создаёт last-success timestamp/marker | `Client_FirstFailedAreaAttemptHasNoSuccessfulRead`, обе области | PASS |
| A5 | Coils восстанавливаются новым чтением в том же сеансе | PartialAreaFailure `(true)`, recovery assertions | PASS |
| A6 | Registers восстанавливаются новым чтением в том же сеансе | PartialAreaFailure `(false)`, recovery assertions | PASS |
| A7 | Равные успешные чтения имеют разные markers | `Client_TracksSuccessfulUnchangedCyclesIndependentlyForBothAreas` | PASS |
| A8 | Failed HR read в том же сеансе запрещает RMW старого слова, write FC requests = 0 | `RegisterBitWrite_RejectsOldShadowAfterFailedReadInSameSession` | PASS |
| A9 | Смена роли/карты отклоняет старые данные; Good требует нового чтения | `Quality_RoleChangeRejectsAcquisitionFromPreviousRole`, `Quality_MapChangeRejectsQueuedOldAcquisitionAndRequiresNewRead`, `Quality_RepeatedStartWithMapChangeClearsProviderBeforeNextRead` | PASS |
| A10 | Server/Both сохраняют read/write и видимость; server не подменяет client RMW | `Quality_BothKeepsServerVisibleWithoutUsingItForClientRmw`, существующие `BothMode_ClientWritesUpdateLocalServerSnapshot`, `ServerMode_SetGetAndSubscribeUseConfiguredMap` | PASS |

Same-session loopback дополнительно проверяет отсутствие обновления failed timestamp/sequence
на нескольких последовательных отказах, неизменность SourceSessionId, Bad/Stale только нужной
точки, Good здоровой точки и неизменный `connection.connected=true`.

## 5. Матрица B1–B14

| ID | Ожидаемое и фактическое поведение | Тест | Статус |
|---|---|---|---|
| B1 | Pre-write 5, write 5, без нового чтения → timeout | `Confirmation_PreWriteEqualValueWithoutNewReadTimesOut` | PASS |
| B2 | Новое равное чтение 5 → Success в Client/Server | `Confirmation_EqualNewReadConfirmsWithoutValueChangeNotification` | PASS |
| B3 | Новое 6 не завершает ожидание; следующее 5 подтверждает | `Confirmation_MismatchingNewReadKeepsWaitingForMatchingRead` | PASS |
| B4 | Равное чтение учитывается без второго change callback | `Confirmation_EqualNewReadConfirmsWithoutValueChangeNotification` | PASS |
| B5 | Coils fail, Registers Good → Coil write не подтверждена | `Confirmation_FailedTargetAreaCannotBeConfirmedByHealthyPeer(true)` | PASS |
| B6 | Registers fail, Coils Good → register write не подтверждена | Тот же тест `(false)` | PASS |
| B7 | Client → Server инвалидирует ожидание | `Confirmation_RoleSwitchInvalidatesPendingWrite(false)` | PASS |
| B8 | Server → Client инвалидирует ожидание | Тот же тест `(true)` | PASS |
| B9 | Endpoint или DataMap изменились → context changed | `Confirmation_MapOrEndpointChangeInvalidatesPendingWrite` | PASS |
| B10 | Cached false не подтверждает отпускание Pulse | `Confirmation_PulseReleaseDoesNotUseCachedFalseAndRisingNeedsNoRead` | PASS |
| B11 | Rising Pulse не ждёт физического true | Тот же тест; существующие transport Pulse tests | PASS |
| B12 | Timeout=0 сохраняет отсутствие ожидания | `Confirmation_ZeroTimeoutAndWriteOnlyDoNotRequireRead(false)` | PASS |
| B13 | Write-only не требует чтения | Тот же тест `(true)` | PASS |
| B14 | Поздний readback не меняет уже завершённый timeout | `Confirmation_ReadArrivingAfterTimeoutCannotChangeFinishedFailure` | PASS |

Дополнительно PASS: задержанный pre-write snapshot, чтение до фактического send при ожидании I/O,
чужая роль в Both, чужой session, A→B→A session, запись до первого snapshot,
синхронный post-write snapshot и свежий false после отпускания Pulse.
22 новых Confirmation cases, 6 AreaFreshness cases и 8 SourceQuality cases: всего 36 новых случаев.

## 6. Точные команды и результаты

Из корня репозитория, PowerShell. Restore solution не потребовался; зависимости не менялись.

```powershell
# Исходный профиль: PASS 98; после этапа 3 с дополнительными фильтрами: PASS 118
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~ModbusTcpServiceTests|FullyQualifiedName~RouteMapModbusAdapterTests|FullyQualifiedName~ModbusRuntimeSmokeTests|FullyQualifiedName~ModbusBitWriterTests'

# Проверка предыдущих ACK-исправлений: PASS 45
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~NotificationsPanelViewModelTests|FullyQualifiedName~ModbusAlarmMonitorTests'

# Промежуточная приёмка P1-04: PASS 118, до начала этапа 4
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ModbusAreaFreshnessTests|FullyQualifiedName~ModbusTcpServiceTests|FullyQualifiedName~RouteMapModbusAdapterTests|FullyQualifiedName~ModbusRuntimeSmokeTests|FullyQualifiedName~ModbusBitWriterTests|FullyQualifiedName~ModbusBitPulseTransportTests'

# P1-03 reproduction: ожидаемый FAIL до исправления; PASS в итоговом прогоне
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~Confirmation_PreWriteEqualValueWithoutNewReadTimesOut'

# Адресные readback cases: PASS 22 после расширения покрытия
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~Confirmation_'

# Все Modbus на первой версии этапа 4: PASS 247; итоговое состояние покрыто полным решением
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-build --no-restore

# Смежные Route Map/ACK: PASS 138
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~RouteMapSignalRuntimeAndMappingTests|FullyQualifiedName~ModbusAlarmMonitorTests|FullyQualifiedName~EquipmentCardViewModelTests|FullyQualifiedName~NotificationsPanelViewModelTests'

# Замечания review: ожидаемый FAIL до исправления, затем PASS
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~Quality_OlderPausedSnapshot'
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~Quality_DelayedNotificationCannotRestoreOldMapAfterReset'
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~Quality_StatusOnlyUpdate'

# Финальная узкая проверка: PASS 7 (после снятия ограничения testhost)
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Quality_StatusOnlyUpdate|FullyQualifiedName~ModbusAreaFreshnessTests'

# Финальная сборка: PASS, 0 errors, 0 warnings в инкрементальном запуске
dotnet build .\DesktopTemplate.slnx --no-restore

# Финальная проверка решения: см. итоговую таблицу ниже
dotnet test .\DesktopTemplate.slnx --no-build --no-restore

git status --short
git diff --check
git diff --cached --check
```

Первоначальная сборка вывела существующие Desktop warnings (nullable, CS0162, AVLN5001),
первый solution build — существующий CS0067 в unit double. Последняя инкрементальная сборка:
0 предупреждений и 0 ошибок. Их отсутствие в последней сборке не означает исправления этих предупреждений.

Один узкий запуск завершился environmental FAIL: vstest не подключился к testhost за 90 секунд.
Минимальный диагностический повтор с `--no-build --no-restore --filter 'FullyQualifiedName~Quality_StatusOnlyUpdate' --blame-hang-timeout 30s --blame-hang-dump-type none`
также не начал тесты и остановлен. Повтор с разрешённым локальным testhost прошёл 7/7;
итоговый solution test выполнен с тем же разрешением. Эти запуски не выданы за продуктовые FAIL/PASS.
Изолированный baseline console первоначально также получил отказ loopback socket; разрешённый
запуск только на 127.0.0.1 успешно воспроизвёл исходный P1-04.

Команды baseline fixture (временный runner, не часть tracked продукта):

```powershell
dotnet restore .\Configurator.Infrastructure.Modbus.Tests\obj\stability-baseline\BaselineRepro.csproj --configfile .\Configurator.Infrastructure.Modbus.Tests\obj\stability-baseline\NuGet.Config
dotnet build .\Configurator.Infrastructure.Modbus.Tests\obj\stability-baseline\BaselineRepro.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project .\Configurator.Infrastructure.Modbus.Tests\obj\stability-baseline\BaselineRepro.csproj --no-build --no-restore
```

## 7. Агенты и review

Оркестратор работал текущей моделью сессии; переключение на запрошенную GPT-6.1 Sol не выполнено,
об этом сообщено до реализации. Конфигурация агентов не менялась.
`protocol_guard` (GPT-6.1 Sol/high) реализовал product-часть последовательно по этапам и проверил
transport ordering, role/session/generation и shadow. `tester` (Luna/medium) подготовил этап 3
с loopback. Родитель подготовил source/readback/concurrency tests и интеграцию.
`reviewer` (GPT-6.1 Sol/high) выполнил независимый read-only review; критических открытых
замечаний не осталось. Одновременно работали не более двух субагентов.

Замечания review закрыты постоянными регрессиями: отложенный старый raw callback,
доставка прежней карты после reset, status-only update поверх задержанного failed-area notification.
Первое проверяет защиту при конкурентном/custom producer; штатные client/server callbacks
сериализованы их I/O gates. Доставка уведомления после remap достижима без custom producer.
A8 усилен same-session тестом: переподключение больше не маскирует очистку shadow.

## 8. Совместимость и ограничения

Сохранены Pulse true→delay→false, ACK предыдущих этапов, Latched, write-only, timeout=0,
UnitId, база адресов, Length/тип, SignalId и Client/Server/Both. Публичные write API сохранены;
snapshot metadata расширены совместимыми init-свойствами. Legacy snapshots декодируются,
но нулевой маркер не является доказательством нового transport read для confirmation.
Пользовательские JSON, DataMap/AlarmMap, Route Map schema и настройки не менялись.
OPC UA код не менялся; его тесты входят в обязательный полный прогон решения.
Новых PLC-команд, retries и автоматического запуска транспорта не добавлено.

NOT RUN: реальное оборудование, нативный UI Windows/ALT Linux, installer/RPM smoke —
не относятся к выполненным изменениям и не запускались. Все протокольные проверки — doubles/loopback.
Независимая гипотеза конкуренции ACK/DataMap RMW не изменялась и не объявлена решённой.
Защитные interlocks и окончательное управление остаются ответственностью PLC.

## 9. Git и окончательная проверка

Работа выполнена на исходной ветке, без push/merge/reset и без изменения пользовательских
профилей. По отдельному запросу пользователя результат этапов 3 и 4 фиксируется коммитом
`fix(modbus): isolate area quality and require fresh write readback`. Исходные изменения `.codex/agents/*.toml`, `.codex/config.toml` и untracked аудит сохранены.
`git diff --check` и `git diff --cached --check` — PASS. Tracked diff просмотрен,
новые файлы проверены отдельно на trailing whitespace и локальные ссылки отчёта.

| Финальная проверка | Результат |
|---|---|
| `dotnet build .\DesktopTemplate.slnx --no-restore` | PASS, 0 ошибок, 0 предупреждений (incremental) |
| Unit | PASS 568 |
| Modbus | PASS 253 |
| OPC UA | PASS 35 |
| Route Map headless UI | PASS 140 |
| Полное решение | **PASS 996, FAIL 0, SKIP 0** |
| `git diff --check` / `git diff --cached --check` | PASS / PASS |
| `python tools/docs/check_markdown_links.py` | PASS: 34 документа, 358 ссылок |
| Локальные ссылки этого отчёта | PASS: 12 целей; новые файлы без trailing whitespace |

Итоговая область реализации: 8 product C# файлов, 4 test C# файла и этот отчёт.
Незакрытых требований A1–A10/B1–B14 и критических замечаний review нет.
После этапа 4 работа остановлена; последующие этапы не начаты.
