# PromFlow.Dispatcher — этап 9: итоговая интеграционная приёмка

Дата: 19 ноября 2026 года. **Final Status: ACCEPTED WITH LIMITATIONS.**

## 1. Исходные данные и область проверки

Репозиторий: https://github.com/zdanilv/PromFlow.Dispatcher.
Ветка: `feature/equipment-settings-add`.
База аудита: `d378a3077d77dd5b00e133b80a1a28f6920bd983`.
HEAD на начало приёмки: `3b75ab209b69f155a68791e86a203a4ae60cf083`.
Вердикт относится к этому HEAD **с дополнительным исправлением и тестами этапа 9**, перечисленными ниже и фиксируемыми одним коммитом вместе с этим отчётом. Исходный HEAD без этих изменений повторную приёмку не прошёл бы из-за P1-07.

Проверены только P0-01, P1-01…P1-09 и P3-01. P2-01/P2-02 не реализовывались. Исходные отчёты использованы для определения требований, а результат установлен по текущему коду, независимому review и новым прогонам. Основной критерий — раздел 12 предоставленного плана реализации; вложенный текст пользователя задаёт объём и форму приёмки. Текст исходного аудита не является отдельным разрешением на изменения.

Прочитаны AGENTS.md, README, актуальные руководства Route Map, Modbus, SignalId, тестирования, расположения конфигурации и карта кода. Предыдущие отчёты не использованы как доказательство прохождения текущих тестов.

Среда: Windows `10.0.26200`, `win-x64`; SDK `10.0.301`, проекты `net10.0`; Avalonia headless; локальные TCP endpoints `127.0.0.1` с временными портами; подставные транспорты и временные профили. Все необходимые `project.assets.json` присутствовали, restore не выполнялся. Решение содержит четыре тестовых проекта; обнаруженные на диске проекты Persistence не входят в это решение и в предмет приёмки.

Оркестратор: текущая модель сессии Codex на базе GPT-6. Точный backend identifier сессия не предоставила; переключение оркестратора на запрошенную GPT-6.1 Sol не выполнялось. Запущены роли со следующими заданными средой моделями:

| Субагент | Модель / effort роли | Работа |
|---|---|---|
| protocol_acceptance / protocol_guard | GPT-6.1 Sol / high | Независимая проверка ACK, Pulse, freshness, readback; целевые тесты |
| independent_review / reviewer | GPT-6.1 Sol / high | Уставки, поколения, конфигурация, shutdown, DialogHost; повторный review дополнительного исправления |
| integration_coverage / tester | GPT-6 Luna / medium | Матрица существующего покрытия, новый loopback-тест и его запуск |

Одновременно работали максимум два субагента. Подмены моделей или сообщения об их недоступности от инструмента запуска не поступали. `.codex` не изменялся.

## 2. Executive Summary

Проверены **11 из 11** находок. Для всех обнаружены адресный код, постоянные регрессии и успешные текущие прогоны. После дополнительного исправления закрыты 1 P0, 9 P1 и 1 P3 в пределах проверенных контрактов. Не подтверждённых исходных исправлений в этой матрице — 0. Открытых критических дефектов reviewer не оставил.

Приёмка выявила один дефект непосредственно в P1-07: отказ сохранения результата автоматической отправки оставлял исходный pending и допускал повтор после reconnect/restart. Он воспроизведён двумя ветками постоянного теста с реальным временным Route Map профилем и исправлен до финального прогона.

Финальная инкрементальная сборка — **PASS**, 0 ошибок, 0 предупреждений. Полная suite — **1085 PASS / 0 FAIL / 0 SKIP**. Исторические 889 тестов не использованы как текущий результат.

Ограничения: нативный UI Windows/ALT Linux не запускался; физические эффекты реального PLC не проверялись. Гарантия автоматической отправки — не более одной попытки, а не гарантированная доставка: авария между долговечным резервированием и вызовом транспорта либо отказ обратного сохранения defer может оставить неизвестный результат без автоматического повтора. Подробности в разделе 11.

## 3. Полная матрица 11 находок

В таблице PASS означает устранение исходного ошибочного пути с регрессиями и смежной проверкой. Границы среды и at-most-once указаны отдельно и не заменены утверждением о проверке реального PLC.

| ID | Приоритет | Исходный дефект | Исправленный код | Постоянные регрессии | Фактический результат | Статус |
|---|---|---|---|---|---|---|
| P0-01 | P0 | Автоматическое уведомление отправляет ACK при выключенном флаге | [общий ACK-контракт журнала](../../Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSessionJournal.cs#L133), обе точки входа | [NotificationsPanelViewModelTests](../../Configurator.Tests.Unit/RouteMap/NotificationsPanelViewModelTests.cs): `Confirm_CurrentActivation_RespectsAcknowledgementEnabled` для panel/monitor и enabled/disabled; `Close_DoesNotRecordOkOrWrite`, `Cancel_DoesNotRecordOkOrWrite` | При disabled локальный OK и журнал сохраняются, writer не вызывается; при enabled допустим один Pulse | PASS |
| P1-01 | P1 | Старый диалог подтверждает новую активацию/адрес и меняет busy/repeat новой | [ModbusAlarmMonitor](../../Configurator.Desktop/Workspace/Alarms/ModbusAlarmMonitor.cs), [панель](../../Configurator.Desktop/Workspace/RouteMap/ViewModels/NotificationsPanelViewModel.cs), контекст журнала | Там же: `Confirm_StaleContext_DoesNotWriteOrReadNewActivation`, `OldFinally_DoesNotResetNewDialogsBusyRepeatOrUnread`, `RuntimeChange_RejectsOldResponse`, **новый** `Concurrent_panel_and_monitor_responses_cannot_ack_a_replacement_context`; [AlarmAcknowledgementUiTests](../../Configurator.Tests.RouteMap.Ui/AlarmAcknowledgementUiTests.cs) | Clear/reactivation/address/flag/remove/disable/runtime change инвалидируют старые ответы; два одновременных старых контекста не ACK-ают замену; новый допустимый контекст работает | PASS |
| P1-02 | P1 | Ошибка отпускания скрыта Success; при потере ответа rising edge нет безопасной попытки false | [ModbusBitWriter](../../Configurator.Infrastructure.Modbus/Runtime/ModbusBitWriter.cs), [client](../../Configurator.Infrastructure.Modbus/Client/ModbusClientService.cs), [server](../../Configurator.Infrastructure.Modbus/Runtime/ModbusServerService.cs) | [ModbusBitWriterTests](../../Configurator.Infrastructure.Modbus.Tests/ModbusBitWriterTests.cs): `PulseAsync_ReportsAndLogsFailedRelease`, `PulseAsync_ResetsAfterAmbiguousActivationWithoutRetryingIt`, `PulseAsync_ReportsBothErrorsWhenActivationAndReleaseAreUnconfirmed`, `PulseAsync_ReleaseTimeoutIsBoundedAndReportsUncertainState`, `PulseAsync_RoleSwitchKeepsReleaseOnOriginalTarget`; [ModbusBitPulseTransportTests](../../Configurator.Infrastructure.Modbus.Tests/ModbusBitPulseTransportTests.cs) | Coil/register-bit, transport loss, оба фронта, cancellation/timeout, соседние биты, замена endpoint проверены; rising не повторяется, неподтверждённый false не Success | PASS |
| P1-03 | P1 | ReadWrite подтверждается равным pre-write кешем | [ModbusTcpService](../../Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs#L960), acquisition sequence и receipt транспорта | [ModbusWriteConfirmationTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusWriteConfirmationTests.cs), класс **ModbusTcpServiceTests**: `Confirmation_PreWriteEqualValueWithoutNewReadTimesOut`, `Confirmation_EqualNewReadConfirmsWithoutValueChangeNotification`, `Confirmation_DelayedPreWriteSnapshotCannotConfirmEvenIfDeliveredAfterSend`, `Confirmation_ReadAcquiredWhileWriteWaitsForIoCannotConfirm`, `Confirmation_BothRejectsWrongRoleAndChangedSession`, `Confirmation_PulseReleaseAcceptsFreshFalseAfterSend` | 5→write 5 без нового чтения — timeout; с новым достоверным чтением 5 — Success; роль/session/map/endpoint и Pulse reset проверены | PASS |
| P1-04 | P1 | Успех одной области освежает значения другой и недостоверный register shadow | [ModbusClientService](../../Configurator.Infrastructure.Modbus/Client/ModbusClientService.cs#L640), [facade](../../Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs#L703), [provider](../../Configurator.Infrastructure.Modbus/RouteMap/ModbusTcpSignalValueProvider.cs#L162) | [ModbusAreaFreshnessTests](../../Configurator.Infrastructure.Modbus.Tests/ModbusAreaFreshnessTests.cs): `Client_PartialAreaFailurePreservesLastSuccessAndRecoveryNeedsNewRead`, `Client_TracksSuccessfulUnchangedCyclesIndependentlyForBothAreas`, `RegisterBitWrite_RejectsOldShadowAfterFailedReadInSameSession`; [ModbusSourceQualityTests](../../Configurator.Infrastructure.Modbus.Tests/ModbusSourceQualityTests.cs); новый loopback-тест | Обе направления частичного отказа, первые failed reads, восстановление новым чтением, Client/Server/Both и отказ RMW на старом shadow покрыты; connected сохраняет действующую семантику | PASS |
| P1-05 | P1 | Save использует состояние соединения на момент открытия | [EquipmentCardParametersDialogViewModel](../../Configurator.Desktop/Dialogs/EquipmentCardParametersDialog/EquipmentCardParametersDialogViewModel.cs#L324) | [EquipmentCardParametersDialogViewModelTests](../../Configurator.Tests.Unit/RouteMap/EquipmentCardParametersDialogViewModelTests.cs): `Save_uses_current_Modbus_connection_instead_of_opening_state`, `Save_reconciles_reconnect_while_pending_is_being_saved_without_duplicate_attempt`, `Save_in_connected_Mock_keeps_local_pending_without_dispatch_or_mapping_validation` | Save следует текущему Modbus-поколению; online/offline изменения и reconnect во время сохранения проверены | PASS |
| P1-06 | P1 | Connected Mock подавляет pending на первом Modbus snapshot | [RouteMapDashboardViewModel](../../Configurator.Desktop/Workspace/RouteMap/ViewModels/RouteMapDashboardViewModel.cs), [RouteMapSignalRuntime](../../Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSignalRuntime.cs) | [RouteMapEquipmentStatusIntegrationTests](../../Configurator.Tests.RouteMap.Ui/RouteMapEquipmentStatusIntegrationTests.cs): `Pending_admin_setpoint_is_sent_once_to_Modbus_after_connected_Mock_in_user_mode`, `Pending_dispatch_waits_for_good_fresh_Modbus_connection`, `Switching_to_modbus_discards_a_queued_mock_snapshot_until_the_first_modbus_poll`; новый loopback-тест | Первый достоверный snapshot нового Modbus-поколения допускает одну обработку; повторные snapshots не повторяют запись; профиль reopened v22 сохраняет результат | PASS |
| P1-07 | P1 | Старая партия переходит на Mock/новое поколение и ложно потребляет pending | [EquipmentParameterAutoDispatcher](../../Configurator.Desktop/Workspace/RouteMap/Settings/EquipmentParameterAutoDispatcher.cs), runtime; дополнительно [TryBeginDispatchAsync](../../Configurator.Desktop/Workspace/RouteMap/Settings/RouteMapEquipmentParameterValueStore.cs#L108) | [EquipmentParameterAutoDispatcherTests](../../Configurator.Tests.Unit/RouteMap/EquipmentParameterAutoDispatcherTests.cs): `Source_switch_stops_old_batch_after_its_in_flight_write`, `Queued_batch_cannot_adopt_a_new_generation_while_waiting_for_the_gate`, `Timeout_after_dispatch_starts_records_the_error_and_is_never_retried`, новые тесты reservation; [RouteMapConfigurationTests](../../Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs#L1369): новый failure/restart | Mock не вызывается; неначатый второй элемент остаётся pending; новые значения защищены; ошибка final persistence больше не приводит к повтору после restart | PASS с ограничением at-most-once |
| P1-08 | P1 | Повреждённые/недоступные настройки окна прерывают startup/Closing | [AppConfigService](../../Configurator.Infrastructure/Services/AppConfigService.cs#L438), [OnWindowClosing](../../Configurator.Desktop/Main/MainWindow.axaml.cs#L57) | [AppConfigServiceTests.Window.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Window.cs): `WindowSettings_InvalidFile_UsesDefaultsWithoutChangingOriginal_ThenPreservesDamagedBytes`, `WindowSettings_FileMoveDeniedByWindowsHandle_PreservesTargetAndDiagnosesFailure`, `WindowSettings_LegacyMigrationFailure_StillReturnsLegacySettingsAndKeepsSource`; [MainWindowLifecycleTests](../../Configurator.Tests.RouteMap.Ui/MainWindowLifecycleTests.cs): `Closing_SettingsFailure_StillDisposesAndStopsActiveRuntimeExactlyOnce`, `Window_StillAppliesAndSavesSizeAndFullScreen` | Повреждённые байты сохраняются; fallback/legacy/атомарность/доступ проверены. Headless Closing продолжает dispose→stop→closed; повторный Closing не создаёт вторую остановку | PASS |
| P1-09 | P1 | Обрыв прямой записи повреждает общий appsettings | [SaveSectionAsync](../../Configurator.Infrastructure/Services/AppConfigService.cs#L97), [SaveSectionsAsync](../../Configurator.Infrastructure/Services/AppConfigService.cs#L317), [WriteSettingsAtomicallyAsync](../../Configurator.Infrastructure/Services/AppConfigService.cs#L214) | [AppConfigServiceTests.Atomic.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Atomic.cs): `AtomicSave_FailureOrCancellationBeforeReplace_PreservesBytesAndDoesNotReload`, `AtomicSave_CommitsAllSectionsBeforeReload_AndPreservesOtherSections`, `AtomicSave_ConcurrentSaves_ReadTheLastCommittedFile`, `AtomicSave_FileMoveDeniedByWindowsHandle_PreservesTargetAndDiagnosesFailure`; legacy cleanup/transfer тесты | При отказе до replace прежний JSON побайтно сохранён и читается заново; reload только после commit; gate, unknown fields, mappings, demo и backup проверены | PASS |
| P3-01 | P3 | Неиспользуемый fallback и недостижимый return | [DialogHostDialogService](../../Configurator.Desktop/Dialogs/DialogHostDialogService.cs#L109) | [DialogHostDialogServiceTests](../../Configurator.Tests.RouteMap.Ui/DialogHostDialogServiceTests.cs): `Confirm_ReturnsActualUserResult`, `Confirm_HostClosesWithoutTypedPayload_ReturnsFalse`, `MissingHost_ThrowsDiagnosticExceptionWithOriginalCause`, `CoordinatorCancellation_IsPropagated`, `OtherCoordinatorException_IsPropagatedUnchanged` | Confirm/cancel/null/missing-host/другие исключения сохранены; CS0162 отсутствует | PASS |

## 4. Протокольная безопасность и совместимость команд

Обе точки завершения ACK передают захваченный контекст активации в журнал. Состояние изменяемой строки после await не используется как основание новой записи. Disabled ACK даёт локальный OK без физического вызова. Cancel/close/clear и устаревшие контексты не записывают ACK. Отдельно проверены горячее изменение AlarmMap, unrelated map change и старый finally, не сбрасывающий busy/repeat новой активации.

ACK Pulse фиксирует исходную цель: роль, endpoint, session/datastore. Rising edge не повторяется автоматически после потери ответа. Независимый ограниченный release вызывается и при неопределённом rising; ошибка release диагностируется без ложного Success. Register-bit фронты читают актуальное слово, сохраняют соседние биты и не переводят false на новую роль после переключения.

Readback требует успешного приобретения данных после транспортного барьера записи, а не изменения значения или доставки старого callback. Равные последовательные чтения различимы. Failed/stale target-area и другая роль/session/map/endpoint не подтверждают запись. Сохранены timeout, write-only/нулевой timeout и короткий Pulse: rising не ждёт снятого импульса; reset требует новое подтверждение false.

Качество и LastSuccessfulReadAt принадлежат конкретной области; failed polling не повышает свежесть. Успешные HoldingRegisters не делают Coils Good. Недостоверный register shadow не допускает RMW. Сохранены server snapshots и действующая connected-семантика, включая Both.

Совместимость операторских команд подтверждена полным Unit/headless прогоном: `EquipmentCardViewModelTests.Momentary_start_and_toggle_stop_require_their_write_modes`, `Start_button_dispatches_one_true_without_changing_stop`, `Stop_toggle_sends_requested_values_only_after_readback`, `ApplyRuntime_updates_checked_state_without_dispatching_commands`, `Power_indicator_uses_only_fresh_runtime_readback`; `RouteMapConfigurationTests.Top_bar_reset_dispatches_single_pulse_true_value`; `TopBarAdminCommandTests.Work_mode_guards_direct_execution_independently_of_settings_visibility`, `Configured_enabled_requires_a_good_fresh_true`, `Connection_loss_blocks_direct_execution`. ПУСК и reset остаются Pulse, СТОП — Latched, PowerStandby — read-only. Snapshot/readback не создаёт обратных команд. UI-проверки не переносят безопасность PLC в интерфейс.

## 5. Route Map, уставки и дополнительное исправление P1-07

Save использует актуальный источник/поколение, общий gate с reconnect и проверку перед вызовом конкретного Modbus dispatcher. В Mock сохраняются локальные SavedValue/pending без mapping validation и физической отправки. Для нового Modbus-поколения требуется его собственный свежий Good connection snapshot. Старый batch не принимает новое поколение, а повторные snapshots не создают бесконтрольных попыток.

### Выявленный дефект

До доработки `CompleteDispatchAsync` вызывался только после транспорта. Если JSON-сохранение результата падало до Publish, manager сохранял прежнюю definition с pending=true. После повторного открытия профиля та же уставка снова отправлялась, в том числе после TimeoutException с неизвестным исходом.

Постоянный тест `Auto_dispatch_result_save_failure_cannot_repeat_a_started_write_after_restart(bool writeFails)` использует реальный manager/store во временном каталоге. Первый транспортный вызов создаёт каталог `<route-map.json>.tmp`, вызывая отказ записи результата; в одной ветке дополнительно имитируется timeout. После удаления помехи профиль открывается заново и повторно запускается dispatcher. **До исправления обе ветки FAIL**: второй транспортный вызов снова создавал помеху и сохранение падало. Одновременные ACK-регрессии в том же прогоне PASS.

### Минимальная доработка

В интерфейс store добавлен совместимый default `TryBeginDispatchAsync`, возвращающий false для хранилищ без гарантии. Реальный store под gate manager проверяет pending и ожидаемый SavedValue, атомарно сохраняет pending=false и сообщение неизвестного результата **до** Modbus-вызова. Ошибка резервирования не допускает транспорт. После фактического вызова сохраняется исход операции; отказ этой записи не восстанавливает разрешение на повтор. Если runtime отклонил ещё не начатый вызов по поколению/доступности/отмене, выполняется `DeferDispatchAsync` с независимым токеном. Существующие expectedSavedValue проверки защищают более новое локальное значение.

Новых полей JSON, версии схемы или новых PLC-команд нет. Тестовые stores и forwarding wrapper дополнены новым контрактом; остальные public members сохранены. Default false намеренно запрещает физическую автоотправку из неподдерживающего reservation хранилища.

Дополнительные постоянные проверки:

- `Auto_dispatch_result_save_failure_cannot_repeat_a_started_write_after_restart(false/true)` — одна запись даже после повторного открытия; сохраняется unknown, v22.
- `Auto_dispatch_reservation_failure_leaves_pending_and_sends_nothing` — неверный expectedSavedValue не резервируется; отказ сохранения сохраняет прежние байты и pending; после устранения отказа одна попытка.
- `Editing_value_while_reservation_waits_does_not_send_the_old_value` — старое значение не отправляется, новое остаётся pending и позднее отправляется отдельно.
- `Source_change_or_cancellation_during_reservation_defers_the_unstarted_attempt(false/true)` — ноль Modbus/Mock вызовов до reconnect, pending возвращён, затем каждый элемент отправляется один раз.

LastDispatchError фиксирует фактическую ошибку подтверждённой попытки; если final persistence недоступно, остаётся ранее сохранённый unknown. При успешном результате ошибка очищается. Безусловная доставка при аварии процесса/самого defer не заявляется.

## 6. Конфигурация и lifecycle

Все пути общего профиля используют один `_saveGate` и запись уникального соседнего temp-файла с replace/move поверх активного. При отмене или write/replace failure до замены прежний валидный JSON не меняется частично; постоянные тесты проверяют bytes и повторное открытие ConfigurationRoot. Concurrent saves читают последний committed файл. Reload следует успешной замене. Cleanup failure не маскирует исходную ошибку commit.

Проверены сохранность неизвестных root/Modbus/point/alarm полей, Modbus.DataMap и AlarmMap, ModbusDemo, backup точного исходника при удалении obsolete modes, повторные сохранения и transfer. Корректность merge установлена не только по коду, но и тестами AppConfigServiceTests/ModbusTcpProfileTransferServiceTests. Никакой действующий пользовательский профиль для этих проверок не открывался на запись.

Window settings: malformed/duplicate/null/invalid size, блокировка файла, directory-instead-of-file и отказ replace дают контролируемый fallback/диагностику. Повреждённый оригинал сохраняется до замены и резервируется в исходных байтах. Legacy migration failure сохраняет legacy и позволяет продолжить с его значениями. Width/Height/IsFullScreen и неизвестные поля сохраняются. Настройки окна не reload-ят runtime-конфигурацию.

Headless lifecycle использует настоящий MainWindow handler и существующий App shutdown с подставным активным runtime: ошибки load/save не останавливают последовательность dispose→stop→closed. Пока stop не завершён, повторный Closing не запускает второй dispose/stop; после завершения разрешается финальное закрытие. Постороннее исключение не подавляется. Реальный loopback runtime отдельно проходит StopAsync/StartClientAsync и асинхронное освобождение ресурсов; нативный UI с реальным runtime в одном процессе не запускался.

Схема остаётся v22 (`CurrentSchemaVersion = 22`). Полные тесты сохраняют round-trip/import/export/миграции/unknown JSON, offline setpoints и install-level WorkMode; пользовательские Windows/Linux profile paths не менялись.

## 7. Интеграционные испытания

Новый [StabilityAcceptanceIntegrationTests](../../Configurator.Infrastructure.Modbus.Tests/StabilityAcceptanceIntegrationTests.cs) соединяет настоящие client/runtime/facade/provider/command dispatcher/RouteMapSignalRuntime с fault-injecting TCP peer на loopback. Store в этом составном сетевом тесте подставной; долговечность профиля и UI проверены отдельными постоянными тестами. Используется один проход основных сетевых стадий, а ACK, generation barrier и Closing проверены существующими сценариями с общими производственными компонентами. Единого нативного процесса, объединяющего все восемь стадий, не запускали.

| Шаг | Условие | Ожидаемый результат | Фактическое наблюдение / доказательство | Статус |
|---|---|---|---|---|
| 1 | Mock, локальная offline-уставка | Отображение по Mock, нет physical write, pending сохранён | Headless `Migrated_bucket_renders_all_mock_codes_and_keeps_route_and_power_independent`; новый loopback: `peer.Writes` пуст после Save/auto в Mock; реальные offline Save/reopen тесты | PASS, составное покрытие |
| 2 | Mock→подключённый локальный Modbus, первый Good snapshot | Одна допустимая попытка, нет повторов snapshots | Loopback получает только значение 5; headless `Pending_admin_setpoint_is_sent_once_to_Modbus_after_connected_Mock_in_user_mode` публикует ещё 20 snapshots, сохраняет одну попытку и проверяет reopen v22 | PASS |
| 3 | Coil read возвращает protocol exception, registers читаются | Coil Bad/Stale, registers Good, плохой Enabled не разрешает команду | Loopback наблюдает failed coil и healthy holding, `connection.connected=true`, число writes остаётся 1; отдельный `Configured_enabled_requires_a_good_fresh_true` проверяет запрет прямого выполнения на Bad/Stale | PASS, составное покрытие |
| 4 | Восстановлен Coil read | Good только после нового чтения, нет дополнительной команды | Loopback проверяет рост SuccessfulReadSequence и Good/fresh; число writes остаётся 1; UI/unit readback tests не записывают команды | PASS |
| 5 | Синтетическая тревога, ACK disabled/enabled, замена активации | Локальный OK без записи либо допустимый Pulse; старый ответ не ACK | Unit обеих точек, headless фонового ответа/reload и новый concurrent panel+monitor тест; два старых ответа дают ноль Pulse, новый контекст даёт один корректный адрес | PASS, отдельные сценарии |
| 6 | Реальный StopAsync, offline Save, StartClientAsync | Offline pending не отправляется; после доступности одна попытка | Loopback: до restart writes=1; после нового snapshot и двух вызовов auto последовательность ровно [5,6]; реальные profile tests отдельно проверяют SavedValue/pending после restart | PASS, составное покрытие |
| 7 | Два pending, первая запись удержана barrier, Modbus→Mock | Вторая запись не попадает в Mock и остаётся pending | `Source_switch_stops_old_batch_after_its_in_flight_write(false/true)`, `Queued_batch_cannot_adopt_a_new_generation_while_waiting_for_the_gate`, новые reservation tests; наблюдаются requests/completions/state | PASS, отдельные сценарии |
| 8 | Ошибка window settings при Closing | Продолжить существующий shutdown и освободить ресурсы | `Closing_SettingsFailure_StillDisposesAndStopsActiveRuntimeExactlyOnce`: dispose/stop ровно один раз, конечный Stopped и closed; loopback tests завершают свои async disposable ресурсы | PASS headless/loopback |

## 8. Сборка и тестирование

### Финальный полный прогон после всех изменений C#

| Проект | Всего | PASS | FAIL | SKIP | Статус |
|---|---:|---:|---:|---:|---|
| Configurator.Tests.Unit | 604 | 604 | 0 | 0 | PASS |
| Configurator.Infrastructure.Modbus.Tests | 285 | 285 | 0 | 0 | PASS |
| Configurator.Tests.RouteMap.Ui | 161 | 161 | 0 | 0 | PASS |
| Configurator.Infrastructure.OpcUa.Tests | 35 | 35 | 0 | 0 | PASS, существующие тесты без изменения |
| **Всего** | **1085** | **1085** | **0** | **0** | **PASS** |

Полная suite завершилась exit code 0. Количества проверены по выводу VSTest и Counters четырёх отдельных TRX в `TestResults/stage9/full`. Обнаружено и исполнено 1085 тестов, ни один не скрыт фильтром полного прогона. Loopback и Avalonia headless доступны. Windows-specific file sharing проверки исполнены, skip нет.

### Все этапы проверки, включая неуспешное воспроизведение

| Прогон | Результат |
|---|---|
| Исходная сборка HEAD | PASS, 0 ошибок, 24 предупреждения: nullable/obsolete/unused и MSB3026 из-за testhost целевого протокольного прогона. Процесс не завершали принудительно; после окончания конкурирующего прогона конфликт исчез |
| Исходные целевые Unit / Modbus / UI | PASS 374 / 191 / 161; 0 FAIL/SKIP |
| Независимые protocol_guard: BitWriter/transport/quality; Confirmation; ACK Unit | PASS 44 / 22 / 45, 0 FAIL/SKIP; эти результаты не прибавляются к full suite |
| Новый reproduction до исправления P1-07 | **FAIL 2 / PASS 2 / SKIP 0**, всего 4: обе persistence/restart ветки упали, оба simultaneous ACK контекста прошли |
| Первые целевые регрессии после исправления | PASS 71, 0 FAIL/SKIP |
| Финальные целевые регрессии с дополнительными отрицательными сценариями | PASS 75, 0 FAIL/SKIP |
| Новый составной loopback | PASS 1, 0 FAIL/SKIP |
| Финальная инкрементальная сборка | PASS, 0 ошибок, 0 предупреждений; не является утверждением об отсутствии warnings при clean rebuild |
| Финальная полная suite | PASS 1085, 0 FAIL/SKIP |
| Markdown checker | PASS: 34 документа, 358 локальных ссылок; отчёты дополнительно проверены отдельно |
| `dotnet --info` | Частичная диагностика: вывел SDK 10.0.301, затем exit 1 из-за доступа к Windows Service Control Manager; это не сбой build/tests. Отдельный `dotnet --version` PASS: 10.0.301 |

Точные команды root (из корня репозитория):

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git diff --stat
git diff --check
git diff --cached --check
git log --oneline d378a3077d77dd5b00e133b80a1a28f6920bd983..HEAD
git diff --stat d378a3077d77dd5b00e133b80a1a28f6920bd983 HEAD
git diff --name-only d378a3077d77dd5b00e133b80a1a28f6920bd983 HEAD
dotnet --info
dotnet --version
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-build --no-restore --filter 'FullyQualifiedName~ModbusAlarmMonitorTests|FullyQualifiedName~RouteMapSessionJournalTests|FullyQualifiedName~NotificationsPanel|FullyQualifiedName~EquipmentCardParametersDialogViewModelTests|FullyQualifiedName~RouteMapSignalRuntimeAndMappingTests|FullyQualifiedName~EquipmentCardViewModelTests|FullyQualifiedName~RouteMapConfigurationTests|FullyQualifiedName~EquipmentParameterAutoDispatcherTests' --logger 'trx;LogFileName=unit-targeted.trx' --results-directory .\TestResults\stage9
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ModbusBitWriterTests|FullyQualifiedName~ModbusTcpServiceTests|FullyQualifiedName~ModbusRuntimeSmokeTests|FullyQualifiedName~RouteMapModbusAdapterTests|FullyQualifiedName~AppConfigServiceTests|FullyQualifiedName~ModbusTcpProfileTransferServiceTests|FullyQualifiedName~ModbusAreaFreshnessTests|FullyQualifiedName~ModbusSourceQualityTests|FullyQualifiedName~ModbusBitPulseTransportTests' --logger 'trx;LogFileName=modbus-targeted.trx' --results-directory .\TestResults\stage9
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-build --no-restore --logger 'trx;LogFileName=ui-targeted.trx' --results-directory .\TestResults\stage9
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~Auto_dispatch_result_save_failure_cannot_repeat|FullyQualifiedName~Concurrent_panel_and_monitor_responses' --logger 'trx;LogFileName=reproduction.trx' --results-directory .\TestResults\stage9
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~EquipmentParameterAutoDispatcherTests|FullyQualifiedName~EquipmentCardParametersDialogViewModelTests|FullyQualifiedName~Auto_dispatch_result_save_failure_cannot_repeat|FullyQualifiedName~Concurrent_panel_and_monitor_responses' --logger 'trx;LogFileName=fix-regressions.trx' --results-directory .\TestResults\stage9
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~EquipmentParameterAutoDispatcherTests|FullyQualifiedName~EquipmentCardParametersDialogViewModelTests|FullyQualifiedName~Auto_dispatch_result_save_failure_cannot_repeat|FullyQualifiedName~Auto_dispatch_reservation_failure|FullyQualifiedName~Concurrent_panel_and_monitor_responses' --logger 'trx;LogFileName=fix-final-regressions.trx' --results-directory .\TestResults\stage9
dotnet build .\DesktopTemplate.slnx --no-restore -fl '-flp:logfile=TestResults/stage9/build-final.log;verbosity=normal'
dotnet test .\DesktopTemplate.slnx --no-build --no-restore --logger trx --results-directory .\TestResults\stage9\full
python tools/docs/check_markdown_links.py
git diff d378a30 HEAD --check
git diff --check
git diff --cached --check
git status --short
```

Команды субагентов:

```powershell
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~ModbusBitWriterTests|FullyQualifiedName~ModbusBitPulseTransportTests|FullyQualifiedName~ModbusAreaFreshnessTests|FullyQualifiedName~ModbusSourceQualityTests|FullyQualifiedName~ModbusWriteConfirmationTests'
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~Confirmation_'
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-build --no-restore --filter 'FullyQualifiedName~ModbusAlarmMonitorTests|FullyQualifiedName~NotificationsPanelViewModelTests'
dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~StabilityAcceptanceIntegrationTests' --logger 'trx;LogFileName=stability-integration.trx' --results-directory .\TestResults\stage9
```

Важная проверка фильтра: `ModbusWriteConfirmationTests.cs` объявляет `partial class ModbusTcpServiceTests`. Фильтр по имени файла не выбирает эти тесты. Они действительно исполнены отдельным `Confirmation_` (22 теста), root-фильтром `ModbusTcpServiceTests` и полным решением.

Сырые TRX, включая неудачное воспроизведение, сохранены в игнорируемом `TestResults/stage9`; лог финальной сборки — `TestResults/stage9/build-final.log`. Эти локальные артефакты не добавлены в tracked источники.

## 9. Независимый review и взаимное влияние исправлений

Protocol reviewer независимо проверил обе ACK-точки, context invalidation/finally/repeat, captured target, bounded release, живой register RMW, source/session и acquisition/write barrier. Дефектов реализации P0-01/P1-01…04 не выявил. Выделил отсутствие прямого теста одновременных panel/monitor контекстов; пробел закрыт новым постоянным тестом с reactivation и изменением ACK-адреса.

Configuration/lifecycle reviewer независимо проверил текущую связь Save с runtime, first-snapshot переход, pending generation, AppConfig atomic merge, window fallback/legacy/Closing и DialogHost. Нашёл описанный P1-07 persistence defect и подтвердил его достижимость реальным store. После минимальной доработки и добавления отрицательных регрессий повторный read-only review: **открытых критических дефектов в проверенной области нет**. Reviewer не выдавал собственные неисполненные тесты за PASS; приведённые 75 и 1085 — прогоны root.

Проверены связи: ACK repeat не сброшен старым finally; Pulse сохраняет роль/endpoint и короткий rising; раздельное качество не ломает server/Both; readback не блокирует короткий Pulse, но не использует кеш для reset; source generation не пропускает первый допустимый pending; gate удерживается до сохранения результата; atomic merge сохраняет посторонние секции; ошибка window settings не меняет существующий shutdown; удаление fallback сохраняет DialogHost результат и ошибки.

Новая интерфейсная возможность не меняет JSON/PLC-пакеты. Для всех известных forwarding stores, участвующих в автоотправке тестов, новый вызов передан реальному store или реализован в fixture. Неподдерживаемое хранилище безопасно возвращает false.

## 10. Git, границы и файлы

База является предком HEAD. Исправления предыдущих этапов находятся в пяти коммитах:

- `f24c50c` — безопасные ACK-контексты активации.
- `f1c95dc` — fail-closed отпускание ACK Pulse.
- `ed08ff9` — независимое качество областей и новый readback.
- `fc43c4d` — текущее Modbus-поколение и уставки.
- `3b75ab2` — атомарные профили, window shutdown и DialogHost.

Их diff к базе: 47 файлов, 5992 добавления, 551 удаление. Полный список приведён в приложении A. Это изменения реализации до текущей приёмки, а не созданные заново правки.

Исходный рабочий статус до этапа 9 содержал только следующие пользовательские элементы:

```text
 M .codex/agents/architect_escalation.toml
 M .codex/agents/implementer.toml
 M .codex/agents/protocol_guard.toml
 M .codex/agents/reviewer.toml
 M .codex/config.toml
?? docs/reports/StabilityAudit_d378a30_2026-11-18.ru.md
```

Они сохранены; содержимое не редактировалось root или субагентами. У `.codex/config.toml` имевшийся diff увеличивает concurrent_threads с 2 до 4; приёмка всё равно соблюдала лимит двух одновременно. У четырёх role TOML git status отмечает изменение, но content diff пуст; приписывать им новые изменения этапа 9 нельзя. Staged diff пуст. Незакоммиченный исторический аудит сохранён без правки.

Изменения непосредственно этапа 9:

| Файл | Назначение |
|---|---|
| [EquipmentParameterAutoDispatcher.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/EquipmentParameterAutoDispatcher.cs) | Резервирование допустимой попытки до транспорта; defer отклонённой неначатой |
| [IEquipmentParameterValueStore.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/IEquipmentParameterValueStore.cs) | Default fail-closed reservation contract |
| [RouteMapEquipmentParameterValueStore.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/RouteMapEquipmentParameterValueStore.cs) | Долговечный CAS pending/SavedValue без изменения схемы |
| [EquipmentParameterAutoDispatcherTests.cs](../../Configurator.Tests.Unit/RouteMap/EquipmentParameterAutoDispatcherTests.cs) | Отрицательные barrier tests и обновлённый store fixture |
| [RouteMapConfigurationTests.cs](../../Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs) | Failure/restart на настоящем временном профиле и reservation failure |
| [NotificationsPanelViewModelTests.cs](../../Configurator.Tests.Unit/RouteMap/NotificationsPanelViewModelTests.cs) | Два одновременных устаревших контекста ACK |
| [EquipmentCardParametersDialogViewModelTests.cs](../../Configurator.Tests.Unit/RouteMap/EquipmentCardParametersDialogViewModelTests.cs) | Адаптация test stores к reservation contract |
| [EquipmentCardParametersDialogTests.cs](../../Configurator.Tests.RouteMap.Ui/EquipmentCardParametersDialogTests.cs) | Forward нового store contract в headless fixture |
| [StabilityAcceptanceIntegrationTests.cs](../../Configurator.Infrastructure.Modbus.Tests/StabilityAcceptanceIntegrationTests.cs) | Новый локальный сетевой составной сценарий |
| Этот отчёт | Матрица, доказательства, ограничения и итоговый вердикт |

Итоговая проверка diff подтверждает:

- OPC UA infrastructure/contracts/tests/runtime не менялись, P2-01/P2-02 не выполнялись. Общий DialogHost helper в предшествующем этапе меняет вызовы всех диалогов, включая generic OPC editor, только удалением неиспользуемого fallback; SDK/OPC-контракты этим не затронуты.
- `schemaVersion` и CurrentSchemaVersion не менялись; v22 подтверждена кодом и reopen тестами.
- Рабочие физические mappings/seed JSON не изменялись. Адреса новых тестов синтетические и живут только в fixture/loopback.
- Нет package/SDK/dependency upgrades, packaging/version changes, новых экранов, изменения UX, PLC command model или посторонней оптимизации.
- AGENTS.md, Codex config, реальные пользовательские профили не редактировались.
- `git diff --check`, `git diff --cached --check`, `git diff d378a30 HEAD --check` — PASS. Git сообщает лишь нормализацию LF→CRLF в части новых строк; whitespace errors отсутствуют.
- Во время приёмки commit, push, merge, reset/clean, installer execution, создание VM и публикация не выполнялись. Последующая фиксация этапа отдельным коммитом разрешена пользователем; push не запрашивался.

## 11. Ограничения и непроверенные условия

| Условие | Статус | Точная граница |
|---|---|---|
| Нативный Windows UI smoke с пользовательским профилем | NOT RUN | Подготовленная изолированная native smoke-среда в сессии не использовалась; вместо неё исполнены настоящие handlers/VM/окна в Avalonia headless и временные профили |
| ALT Linux 11.1 native UI / filesystem smoke | NOT RUN | В текущей Windows-сессии нет предоставленной ALT среды; VM/установку не создавали. Portable paths проверены существующими тестами и отсутствием изменений path-кода |
| Установщики, installed upgrade | NOT RUN | Не выполнялись и не требуются для изменения packaging, поскольку packaging не менялся; отдельного разрешения на исполнение нет |
| Физический PLC и реальная промышленная сеть | NOT RUN | Все записи были loopback или in-memory; механический эффект и PLC-interlocks не подтверждались и остаются ответственностью PLC |
| Power loss / принудительное убийство процесса в момент replace | NOT RUN | Динамически проверены контролируемые ошибки до replace, Windows handle-denied и повторное чтение JSON. Сбой питания и гарантии fsync/durability ОС не моделировались |
| Авария после reservation до invocation; отказ самого defer | NOT RUN для составного аварийного сценария; граница установлена review | Может сохраниться pending=false + unknown, даже если транспорт не начался. Автоматического повтора не будет; гарантированная доставка не заявляется, требуется установление фактического состояния перед новой операторской попыткой |
| Единый native процесс со всеми восемью стадиями | NOT RUN | Основная сетевая последовательность выполнена составным loopback-тестом; ACK, UI guard, durable profile и Closing проверены отдельными постоянными интеграциями, с явными границами в таблице раздела 7 |

Нет заблокированного обязательного build/test прогона и нет environmental skips в full suite. Ограничение `dotnet --info` не мешало SDK/build/test. Не проводились новый полный аудит проекта, дополнительные performance/memory исследования, гипотетическая ACK/DataMap RMW-конкуренция или OPC UA исправления.

## 12. Финальный вердикт

**Final Status: ACCEPTED WITH LIMITATIONS.**

Все 11 исходных находок имеют исправление и постоянное автоматическое покрытие; текущая полная suite прошла. P1-07 потребовала описанной дополнительной доработки, воспроизводимой до фикса и проверенной после него. Независимый review не оставил критических дефектов в проверенной области. Совместимость v22, профилей, пользовательских режимов и операторских команд подтверждена доступными регрессиями; запрещённые области не изменены.

Полное безусловное ACCEPTED не присваивается из-за перечисленных native/platform/crash/physical ограничений. Вердикт не гарантирует exactly-once доставку уставок или физическое отпускание PLC-бита после потери транспорта. Необходимость автоматического повтора из неизвестного состояния не вводилась. Незакрытых требований по исправлению исходных 11 находок в проверенном штатном и контролируемом error-контракте не осталось; ограничения приёмки перечислены явно.

## 13. Фиксация этапа

После завершения приёмки пользователь отдельно поручил зафиксировать этап и создать коммит. Исправление P1-07, постоянные регрессии, составной loopback-тест и этот отчёт включены в один коммит с сообщением `fix(stability): complete stage 9 integration acceptance`. Прежние пользовательские изменения `.codex` и исходный незакоммиченный отчёт аудита исключены из коммита.

Перед фиксацией повторно проверены ветка, HEAD, полный состав изменений и whitespace. Код после успешного прогона 1085 тестов не менялся; обновлено только описание фиксации в отчёте, поэтому повторный build/test для операции commit не выполнялся. Итоговый статус приёмки остаётся **ACCEPTED WITH LIMITATIONS**.

## Приложение A. Полный diff предыдущих этапов к базе аудита

- [Configurator.Application/Services/Modbus/Runtime/ModbusDataSnapshot.cs](../../Configurator.Application/Services/Modbus/Runtime/ModbusDataSnapshot.cs)
- [Configurator.Application/Services/Modbus/Runtime/ModbusSnapshot.cs](../../Configurator.Application/Services/Modbus/Runtime/ModbusSnapshot.cs)
- [Configurator.Desktop/Dialogs/DialogHostDialogService.cs](../../Configurator.Desktop/Dialogs/DialogHostDialogService.cs)
- [Configurator.Desktop/Dialogs/EquipmentCardParametersDialog/EquipmentCardParametersDialogViewModel.cs](../../Configurator.Desktop/Dialogs/EquipmentCardParametersDialog/EquipmentCardParametersDialogViewModel.cs)
- [Configurator.Desktop/Main/MainWindow.axaml.cs](../../Configurator.Desktop/Main/MainWindow.axaml.cs)
- [Configurator.Desktop/Workspace/Alarms/ModbusAlarmMonitor.cs](../../Configurator.Desktop/Workspace/Alarms/ModbusAlarmMonitor.cs)
- [Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSessionJournal.cs](../../Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSessionJournal.cs)
- [Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSignalRuntime.cs](../../Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSignalRuntime.cs)
- [Configurator.Desktop/Workspace/RouteMap/Settings/EquipmentParameterAutoDispatcher.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/EquipmentParameterAutoDispatcher.cs)
- [Configurator.Desktop/Workspace/RouteMap/Settings/IEquipmentParameterValueStore.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/IEquipmentParameterValueStore.cs)
- [Configurator.Desktop/Workspace/RouteMap/Settings/RouteMapEquipmentParameterValueStore.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/RouteMapEquipmentParameterValueStore.cs)
- [Configurator.Desktop/Workspace/RouteMap/ViewModels/NotificationsPanelViewModel.cs](../../Configurator.Desktop/Workspace/RouteMap/ViewModels/NotificationsPanelViewModel.cs)
- [Configurator.Desktop/Workspace/RouteMap/ViewModels/RouteMapDashboardViewModel.cs](../../Configurator.Desktop/Workspace/RouteMap/ViewModels/RouteMapDashboardViewModel.cs)
- [Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Atomic.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Atomic.cs)
- [Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Window.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Window.cs)
- [Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusAreaFreshnessTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusAreaFreshnessTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusBitPulseTransportTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusBitPulseTransportTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusBitWriterTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusBitWriterTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusSourceQualityTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusSourceQualityTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusTcpProfileTransferServiceTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusTcpProfileTransferServiceTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusTcpServiceTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusTcpServiceTests.cs)
- [Configurator.Infrastructure.Modbus.Tests/ModbusWriteConfirmationTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusWriteConfirmationTests.cs)
- [Configurator.Infrastructure.Modbus/Client/ModbusClientService.cs](../../Configurator.Infrastructure.Modbus/Client/ModbusClientService.cs)
- [Configurator.Infrastructure.Modbus/RouteMap/ModbusTcpSignalValueProvider.cs](../../Configurator.Infrastructure.Modbus/RouteMap/ModbusTcpSignalValueProvider.cs)
- [Configurator.Infrastructure.Modbus/Runtime/IModbusBitPulseTarget.cs](../../Configurator.Infrastructure.Modbus/Runtime/IModbusBitPulseTarget.cs)
- [Configurator.Infrastructure.Modbus/Runtime/IModbusObservedWriter.cs](../../Configurator.Infrastructure.Modbus/Runtime/IModbusObservedWriter.cs)
- [Configurator.Infrastructure.Modbus/Runtime/ModbusBitWriter.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusBitWriter.cs)
- [Configurator.Infrastructure.Modbus/Runtime/ModbusObservationSequence.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusObservationSequence.cs)
- [Configurator.Infrastructure.Modbus/Runtime/ModbusServerService.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusServerService.cs)
- [Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs](../../Configurator.Infrastructure.Modbus/Runtime/ModbusTcpService.cs)
- [Configurator.Infrastructure/DependencyInjection.cs](../../Configurator.Infrastructure/DependencyInjection.cs)
- [Configurator.Infrastructure/Properties/AssemblyInfo.cs](../../Configurator.Infrastructure/Properties/AssemblyInfo.cs)
- [Configurator.Infrastructure/Services/AppConfigService.cs](../../Configurator.Infrastructure/Services/AppConfigService.cs)
- [Configurator.Tests.RouteMap.Ui/AlarmAcknowledgementUiTests.cs](../../Configurator.Tests.RouteMap.Ui/AlarmAcknowledgementUiTests.cs)
- [Configurator.Tests.RouteMap.Ui/DialogHostDialogServiceTests.cs](../../Configurator.Tests.RouteMap.Ui/DialogHostDialogServiceTests.cs)
- [Configurator.Tests.RouteMap.Ui/EquipmentCardParametersDialogTests.cs](../../Configurator.Tests.RouteMap.Ui/EquipmentCardParametersDialogTests.cs)
- [Configurator.Tests.RouteMap.Ui/MainWindowLifecycleTests.cs](../../Configurator.Tests.RouteMap.Ui/MainWindowLifecycleTests.cs)
- [Configurator.Tests.RouteMap.Ui/RouteMapEquipmentStatusIntegrationTests.cs](../../Configurator.Tests.RouteMap.Ui/RouteMapEquipmentStatusIntegrationTests.cs)
- [Configurator.Tests.Unit/ModbusAlarmMonitorTests.cs](../../Configurator.Tests.Unit/ModbusAlarmMonitorTests.cs)
- [Configurator.Tests.Unit/RouteMap/EquipmentCardParametersDialogViewModelTests.cs](../../Configurator.Tests.Unit/RouteMap/EquipmentCardParametersDialogViewModelTests.cs)
- [Configurator.Tests.Unit/RouteMap/EquipmentParameterAutoDispatcherTests.cs](../../Configurator.Tests.Unit/RouteMap/EquipmentParameterAutoDispatcherTests.cs)
- [Configurator.Tests.Unit/RouteMap/NotificationsPanelViewModelTests.cs](../../Configurator.Tests.Unit/RouteMap/NotificationsPanelViewModelTests.cs)
- [Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs](../../Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs)
- [docs/reports/StabilityFixes_Stages3-4_f1c95dc_2026-11-19.ru.md](../../docs/reports/StabilityFixes_Stages3-4_f1c95dc_2026-11-19.ru.md)
- [docs/reports/StabilityFixes_Stages6-8_2026-11-19.ru.md](../../docs/reports/StabilityFixes_Stages6-8_2026-11-19.ru.md)
- [docs/reports/Stage5_Stability_Fixes_2026-11-19.ru.md](../../docs/reports/Stage5_Stability_Fixes_2026-11-19.ru.md)
