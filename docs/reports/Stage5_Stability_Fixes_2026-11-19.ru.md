# Этап 5 — P1-05, P1-06, P1-07

Дата: 19.11.2026. Ветка: `feature/equipment-settings-add`.
Базовый HEAD этапа до фиксации: `ed08ff9a5ccdc1f8001a55b7ef16bed310c29775`.
База аудита: `d378a3077d77dd5b00e133b80a1a28f6920bd983`.

## Результат

Реализованы три исправления этапа 5. Финальная сборка — PASS; полное решение —
**1028/1028 PASS**, без пропусков. Независимый reviewer не обнаружил оставшихся
блокирующих дефектов в изменениях этапа 5. Работа остановлена до этапа 6.

- **P1-05:** диалог при Save получает актуальный доступный Modbus-контекст,
  проверяет поколение после валидации и перед записью. Снимок открытия служит
  исходными данными строк. Offline/Mock сохраняют локально с pending. После
  сохранения или отложения выполняется одна согласующая попытка через существующий
  auto dispatcher: reconnect, прошедший во время записи файла, не теряется.
- **P1-06:** доступность для pending относится к Modbus-поколению. Инвалидация
  источника сбрасывает прежнее состояние, но не отправляет уставки. Первый живой
  Good/non-Stale Modbus-снимок запускает обработку независимо от connected у Mock.
  Повторные снимки и non-live null-инвалидация того же поколения не переоружают её.
- **P1-07:** auto dispatcher захватывает поколение до ожидания общего допуска,
  проверяет его после ожидания, перед каждым элементом и после завершения записи.
  Dispatcher выбирается под тем же runtime-lock, что проверяет поколение и
  доступность. Старый batch не продолжает работу через Mock или новый Modbus.

## Изменённые файлы

Пути относительно корня репозитория; пользовательские изменения `.codex` в таблицу
реализации не включены.

| Файл | Назначение | Находка | Проверка |
|---|---|---|---|
| [EquipmentCardParametersDialogViewModel.cs](../../Configurator.Desktop/Dialogs/EquipmentCardParametersDialog/EquipmentCardParametersDialogViewModel.cs) | Актуальный контекст Save, сериализация попыток, defer неначатых значений, однократное согласование | P1-05/07 | Диалог, включая двухстрочный async-барьер |
| [RouteMapSignalRuntime.cs](../../Configurator.Desktop/Workspace/RouteMap/Services/RouteMapSignalRuntime.cs) | Кеш актуального снимка, Modbus-допуск, общий semaphore, dispatch с guard | P1-05/06/07 | Runtime, auto dispatcher, headless |
| [EquipmentParameterAutoDispatcher.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/EquipmentParameterAutoDispatcher.cs) | Фиксация поколения до gate, проверки каждой записи, обработка отмены и исходов | P1-05/07 | 18 новых сценариев auto dispatcher |
| [IEquipmentParameterValueStore.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/IEquipmentParameterValueStore.cs) | Добавлен условный DeferDispatchAsync; прежние сигнатуры сохранены, default сообщает об отсутствии поддержки | P1-05/07 | Диалог и store |
| [RouteMapEquipmentParameterValueStore.cs](../../Configurator.Desktop/Workspace/RouteMap/Settings/RouteMapEquipmentParameterValueStore.cs) | Defer с expectedSavedValue через существующий write gate менеджера | P1-05/07 | Реальное хранилище, restart и v22 |
| [RouteMapDashboardViewModel.cs](../../Configurator.Desktop/Workspace/RouteMap/ViewModels/RouteMapDashboardViewModel.cs) | Состояние готовности Modbus по поколению; новые партии не теряются за старой | P1-06/07 | Headless first snapshot, качество, повторные снимки |
| [EquipmentCardParametersDialogViewModelTests.cs](../../Configurator.Tests.Unit/RouteMap/EquipmentCardParametersDialogViewModelTests.cs) | Новые регрессии Save и замечаний review; прежние auto-тесты переведены на реальный runtime | P1-05/07 | 9 новых data-expanded случаев |
| [EquipmentParameterAutoDispatcherTests.cs](../../Configurator.Tests.Unit/RouteMap/EquipmentParameterAutoDispatcherTests.cs) | Новый файл: управляемые барьеры, источники, поколения, исходы и конкуренция | P1-07 | 18 случаев |
| [RouteMapConfigurationTests.cs](../../Configurator.Tests.Unit/RouteMap/RouteMapConfigurationTests.cs) | Stale complete/defer не изменяют новое SavedValue; restart сохраняет v22 | P1-05/07 | 1 новая регрессия и штатные JSON-тесты |
| [EquipmentCardParametersDialogTests.cs](../../Configurator.Tests.RouteMap.Ui/EquipmentCardParametersDialogTests.cs) | Test-store делегирует новый defer реальному хранилищу | P1-05/07 | Штатные user/admin UI-тесты |
| [RouteMapEquipmentStatusIntegrationTests.cs](../../Configurator.Tests.RouteMap.Ui/RouteMapEquipmentStatusIntegrationTests.cs) | Новые first-snapshot/quality регрессии и полный Dashboard → runtime → store → auto путь | P1-06/07 | 4 новых случая; две прежние проверки приведены к требуемой Modbus-семантике |
| Этот отчёт | Результат, проверки и ограничения | Этап 5 | Проверка ссылок и diff |

## Контракты и поток данных

Фактическим источником и поколением владеет `RouteMapSignalRuntime` под `_sync`.
`EquipmentCardViewModel` по-прежнему передаёт сервису диалога исходный снимок и
отображаемое состояние. Сервис разрешает зарегистрированный
`IEquipmentCommandDispatcher`; в Boot это тот же singleton runtime. Диалог получает
из него текущий Modbus-контекст при Save. Для прежних самостоятельных вызовов с
dispatcher без runtime сохранён bool-контракт; неизвестный `IRouteMapSignalRuntime`
не получает разрешения на запись без поддержанного generation-контекста.

`TryGetAvailableModbusGeneration` требует активный Modbus, актуальный снимок,
`connection.connected` типа Bool со значением true, Good и non-Stale. SwitchSource
инвалидирует кеш до подключения провайдера. `SnapshotUpdate.Generation`,
`SourceChanged` и `IsCurrentGeneration` защищают очередь UI от старых обновлений.
Собственного счётчика поколений нет.

Открытие диалога не фиксирует транспортное решение. Online preflight выполняется
для текущего контекста; если поколение изменилось во время проверки, Save выбирает
локальное сохранение. Перед каждым dispatch повторяются валидация и проверка
доступного поколения. `TryDispatchEquipmentParameterAsync` использует существующий
generation-aware `DispatchCoreAsync`, дополнительно требуя доступный Modbus.
Проверка и выбор конкретного dispatcher выполняются атомарно; Mock не выбирается
этим путём.

Общий `EquipmentParameterDispatchGate` сериализует Save, auto batches и сохранение
результата. После освобождения допуска диалог один раз вызывает существующий
auto dispatcher для согласования созданного pending. Параллельный reconnect ждёт
тот же gate и затем перечитывает pending. Поэтому две причины обработки не создают
двух записей одного уже завершённого значения. Таймеров, polling и retry-loop нет.

Online Save сохраняет прежний `pending=false` до физической попытки. Если runtime
отклонил запись до начала, условный `DeferDispatchAsync` возвращает только текущий
и последующие неначатые элементы в pending. Он сравнивает `expectedSavedValue` и
не переписывает новое значение. `finally` сохраняет последующие неначатые элементы
и при ошибке сохранения результата предыдущего. Ошибка defer показывается как
ошибка хранения; успешное создание pending при ней не заявляется.

Offline Save сохраняет `pending=true`. Auto dispatcher снимает pending после
валидной завершённой попытки или определённой ошибки валидации текущего поколения.
Timeout/transport error сохраняются как LastDispatchError без автоматического
повтора. Отмена после вызова Modbus также потребляет попытку; результат сохраняется
с `CancellationToken.None`, затем отмена передаётся вызывающему. Отмена до вызова,
потеря достоверности и устаревшее поколение оставляют неначатые значения pending.
Известный результат начатой записи учитывается и после смены источника, затем
старый batch останавливается. `expectedSavedValue` в completion сохранён.

Формат JSON и `CurrentSchemaVersion=22`, SignalId, адреса, mapping, типы,
направления, Pulse/Latched, TCP lifecycle, autostart и user/admin UX не изменялись.
Observable-обновления Dashboard остаются в UI-thread очереди. Выбор источника
не запускает TCP runtime. К PLC или промышленным endpoint обращений не было.

## Матрица T01–T10

Во всех новых async-сценариях используются управляемые TCS-барьеры с
RunContinuationsAsynchronously; WaitAsync ограничивает зависание теста.
Произвольные задержки для воспроизведения гонок не используются.

| ID | Сценарий | Ожидаемо | Фактически | Статус |
|---|---|---|---|---|
| T01 | Открытие offline, Save после connected | Текущая online-ветка | Save_uses_current_Modbus_connection_instead_of_opening_state: 1 Modbus, 0 Mock, completion без ошибки | PASS |
| T02 | Открытие online, Save после disconnect | Локально, pending | Вторая строка той же theory: 0 записей, pending, локальный статус | PASS |
| T03 | Reconnect во время создания pending | Одна допустимая попытка | Save_reconciles_reconnect_while_pending_is_being_saved_without_duplicate_attempt: Save и reconnect за барьером, ровно 1 запрос/completion | PASS |
| T04 | Connected Mock → уже работающий Modbus | Первый fresh snapshot отправляет | Headless Pending_admin_setpoint_is_sent_once_to_Modbus_after_connected_Mock_in_user_mode: 0 Mock, 1 Modbus, реальный store/restart | PASS |
| T05 | Повторные снимки одного поколения | Без дублей | 20 снимков, non-live null, поздний Mock: attempts=1; отдельные bad/stale проверки | PASS |
| T06 | Modbus → Mock во время A из A/B | Только A начат, B pending | Source_switch_stops_old_batch_after_its_in_flight_write(false): запрос A, completion только A, 0 Mock, B pending | PASS |
| T07 | Возврат в новое поколение при старом batch | Старый batch не принимает новую сессию | Та же theory(true), Queued_batch_cannot_adopt_a_new_generation_while_waiting_for_the_gate и двухстрочный Save-тест | PASS |
| T08 | Сохранение нового значения во время A | Старый результат не снимает новый pending | Editing_values_during_an_attempt_protects_new_values_from_the_old_batch; реальный store Deferred_and_completed_attempts_cannot_overwrite_a_newer_saved_value | PASS |
| T09 | Error, cancellation, stale generation/quality | Предсказуемые pending/error, без транспортного retry | 18 auto-сценариев, validation-switch, dispose, online completion-IO и статус ошибки диалога | PASS |
| T10 | User/admin, offline, Route Map v22 import/export | Совместимость | Штатные Work_mode_controls_rows_and_technical_captions, Editor_and_operator_dialogs_preserve_hidden_setpoints_online_and_offline, V22_round_trips_all_custom_commands_through_save_apply_export_and_import, parameter round-trip и весь UI-проект | PASS |

## Выполненные команды

Все команды выполнялись из корня репозитория. SDK: `10.0.301`. Assets имелись;
restore не понадобился. Ниже перечислены фактические прогоны, включая промежуточный
FAIL и его причину. Все финальные тесты вошли в последний полный прогон.

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git diff
git diff --stat
git log -7 --oneline
dotnet --version

# A — исходная профильная проверка: PASS 91
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~EquipmentCardParametersDialogViewModelTests|FullyQualifiedName~RouteMapSignalRuntimeAndMappingTests'

# B — P1-05: PASS 44
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~EquipmentCardParametersDialogViewModelTests'

# C — P1-06: PASS 4
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter 'FullyQualifiedName~Pending_dispatch|FullyQualifiedName~Switching_to_modbus'

# D — test-author: PASS 14; затем расширен до 18 и проверен E/полным прогоном
dotnet test Configurator.Tests.Unit/Configurator.Tests.Unit.csproj --no-restore --filter FullyQualifiedName~EquipmentParameterAutoDispatcherTests

# E — объединённый профильный прогон: PASS 324
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~EquipmentCardParametersDialogViewModelTests|FullyQualifiedName~EquipmentParameterAutoDispatcherTests|FullyQualifiedName~EquipmentCardViewModelTests|FullyQualifiedName~RouteMapSignalRuntimeAndMappingTests|FullyQualifiedName~RouteMapConfigurationTests'

# F — FAIL: 9 PASS, 2 FAIL; устаревшие ожидания Mock/Modbus исправлены
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter 'FullyQualifiedName~Pending_'

# G — после исправления ожиданий: PASS 11
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter 'FullyQualifiedName~Pending_|FullyQualifiedName~Switching_between_healthy_sources'

# H — дополнительный двухстрочный Save/barrier: PASS 1
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore --filter 'FullyQualifiedName~Save_source_round_trip'

# I — включая same-generation non-live null: PASS 4
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter 'FullyQualifiedName~Pending_dispatch|FullyQualifiedName~Pending_admin'

# J — выполнялась дважды: обе сборки PASS, 0 ошибок/предупреждений
dotnet build .\DesktopTemplate.slnx --no-restore

# K — сначала PASS 1027, после H/I и финального guard: PASS 1028
dotnet test .\DesktopTemplate.slnx --no-build --no-restore

git diff --check
git diff --cached --check
git diff --name-only
git diff --numstat
git status --short
```

| Финальный проект | Пройдено | Не пройдено | Пропущено | Итог |
|---|---:|---:|---:|---|
| Configurator.Tests.Unit | 596 | 0 | 0 | PASS |
| Configurator.Tests.RouteMap.Ui | 144 | 0 | 0 | PASS |
| Configurator.Infrastructure.Modbus.Tests | 253 | 0 | 0 | PASS |
| Configurator.Infrastructure.OpcUa.Tests | 35 | 0 | 0 | PASS |
| Всего | 1028 | 0 | 0 | PASS |

В промежуточном F две прежние проверки ожидали: (1) отсутствие запуска pending
при смене healthy Mock на healthy Modbus; (2) запуск pending по Mock-readback при
изменении видимости. Эти ожидания противоречат P1-06. Они заменены на один запуск
для нового Modbus-поколения и отсутствие запуска от Mock соответственно.

Первая компиляция и компиляции после правок показывали существующие предупреждения
nullable, Authorization AXAML, недостижимый fallback диалогов и неиспользуемое
событие теста. Они не исправлялись вне scope. Финальные инкрементальные сборки
выдали 0 предупреждений; это не утверждение о clean rebuild без предупреждений.

Самостоятельные полные `dotnet test` отдельных Unit/UI-проектов отдельно от
полного решения — NOT RUN как дублирование: оба полностью выполнены командой K.
Restore — NOT RUN, зависимости не менялись.

## Субагенты и review

- Запрошенная роль `protocol_guard`, модель роли GPT-6.1 Sol/high: попытка запуска
  завершилась сообщением о лимите использования; результатов анализа от неё нет.
  Проверку исходного контракта выполнил ведущий агент текущей сессии.
- Автор узких тестов `pending_tests`: фактически GPT-6.2 Sol/high, роль default
  с задачей test-author как доступная замена; владел только новым тестовым файлом.
  Написал 18 случаев, первые 14 запустил самостоятельно; все 18 прошли в E и K.
- Независимый `reviewer`: фактически роль reviewer, GPT-6.1 Sol/high, read-only.
  Нашёл три замечания: новый online-replay после completion-IO failure, stale
  validation, неверное обещание retry в статусе. Все устранены и покрыты тестами.
  По его рекомендации добавлен отдельный двухстрочный барьер диалога. Повторный
  review и проверка последнего узкого delta блокеров не выявили. Сам reviewer
  сборку и тесты не запускал.
- `.codex` и модели ролей не изменялись. Одновременно работали не более двух
  субагентов; совместного редактирования одного файла не было.

## Ограничения и Git

Существовавший до этапа 5 случай: offline/auto write уже начат, а последующее
`CompleteDispatchAsync` не удаётся сохранить из-за ошибки хранилища. Исходный
pending может остаться в файле и стать кандидатом следующего reconnect. Этот
сбой хранения не исправлен; гарантия отсутствия retry здесь относится к штатному
сохранению результата transport error/timeout/cancellation. Новый online-путь
не добавляет такой возможности, поскольку сохраняет pending=false до отправки.
Механизм durable attempt journal не вводился. Это ограничение не следует считать
проверенной гарантией доставки или переносить на PLC ответственность интерфейса.

Нативный UI Windows, ALT Linux, установщики и обновление установленного приложения
— NOT RUN; выполнены headless UI и штатные mock/loopback tests. Новых обращений к
промышленным endpoint и изменений OPC UA не было. Исходные дефекты перед правкой
сверены по коду; отдельный запуск новых регрессий на исходном HEAD не выполнялся.

Рабочая ветка сохранена. Исходные изменения `.codex/agents/architect_escalation.toml`,
`implementer.toml`, `protocol_guard.toml`, `reviewer.toml`, `.codex/config.toml` и
пользовательский untracked `StabilityAudit_d378a30_2026-11-18.ru.md` не редактировались.
`git diff --check` и `git diff --cached --check` — PASS. Git сообщал только о
будущей нормализации LF→CRLF, ошибок whitespace нет. По отдельному запросу
пользователя этап фиксируется коммитом, включающим реализацию, тесты и этот отчёт.
Пользовательские изменения `.codex` и исходный аудит остаются вне коммита.
Push, merge, reset, смена ветки, версии, NuGet или schema не выполнялись.
