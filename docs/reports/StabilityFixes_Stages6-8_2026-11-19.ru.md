# Исправления стабильности PromFlow.Dispatcher: этапы 6–8

Дата: 19.11.2026. Ветка: `feature/equipment-settings-add`.
HEAD реализации и проверки до коммита: `fc43c4de7dd7cd8387b7dbc63c3f4f9fe269b966`.
База аудита: `d378a3077d77dd5b00e133b80a1a28f6920bd983`.
Среда проверки: Windows, .NET SDK `10.0.301`, .NET 10.

## 1. Результат и границы

Реализованы P1-09, P1-08 и P3-01 в указанной последовательности. Автоматизированные критерии C01–C12, W01–W12 и D01–D05 проверены. Финальная сборка успешна; полная suite: **1076 PASS, 0 FAIL, 0 SKIP**. Независимый reviewer подтвердил устранение обоих замечаний. Этап 9 не начинался.

Запрос взят из приложенного пользователем текста. План и аудит из Downloads использованы как технические источники, с проверкой контрактов по текущему коду. Ветка не откатывалась к ревизии аудита. История содержит предыдущие исправления: `f24c50c` (тревоги), `f1c95dc` (ACK), `ed08ff9` (качество областей/readback), `fc43c4d` (поколения отправки уставок); их существующие тесты вошли в общую suite.

- **P1-09:** прямые записи из `SaveSectionAsync` и `SaveSectionsAsync` заменены вызовом существующего `WriteSettingsAtomicallyAsync`. Добавлены диагностика файловых ошибок и best-effort cleanup, сохраняющий исходное исключение.
- **P1-08:** чтение настроек окна обрабатывает JSON/I/O/access ошибки с диагностикой и defaults. Запись атомарна; повреждённые исходные байты сохраняются перед заменой. Closing перехватывает только ошибки настроек, позволяя существующему shutdown продолжиться.
- **P3-01:** удалены неиспользуемый параметр `fallback`, его аргументы и недостижимый `return`. Результаты, отмена и контракты исключений сохранены.

Нативный GUI smoke Windows/ALT Linux и реальное аварийное убийство процесса: **NOT RUN**. Установщики, VM и реальные PLC не использовались.

## 2. Изменённые файлы

| Файл | Изменение | Находка | Проверка |
|---|---|---|---|
| [AppConfigService.cs](../../Configurator.Infrastructure/Services/AppConfigService.cs) | Атомарная запись секций; fallback/валидация окна; диагностическая копия; cleanup и логирование | P1-09, P1-08 | 48 профильных тестов; полная suite |
| [DependencyInjection.cs](../../Configurator.Infrastructure/DependencyInjection.cs) | Передача зарегистрированного ILogger в конфигурационный сервис | P1-08, P1-09 | Solution build/test |
| [AssemblyInfo.cs](../../Configurator.Infrastructure/Properties/AssemblyInfo.cs) | Friend assembly для внутренней точки инъекции и проверки диагностики | P1-09, P1-08 | Компиляция Infrastructure и тестов |
| [MainWindow.axaml.cs](../../Configurator.Desktop/Main/MainWindow.axaml.cs) | Узкий обработчик ошибок чтения/сохранения при Closing | P1-08 | 9 headless lifecycle-тестов |
| [DialogHostDialogService.cs](../../Configurator.Desktop/Dialogs/DialogHostDialogService.cs) | Удаление параметра, аргументов и мёртвого return | P3-01 | 8 headless dialog-тестов; сборка без соответствующего CS0162 |
| [AppConfigServiceTests.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.cs) | Backup проверяется для обоих методов; legacy сохраняет неизвестное поле | P1-09, P1-08 | Профильные тесты |
| [AppConfigServiceTests.Atomic.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Atomic.cs) | Отказы, отмена, commit/reload, конкуренция, cleanup, реальный отказ File.Move | P1-09 | Профильные тесты |
| [AppConfigServiceTests.Window.cs](../../Configurator.Infrastructure.Modbus.Tests/AppConfigServiceTests.Window.cs) | Невалидный JSON/размеры/дубликаты, I/O/access, сохранение байтов, миграция | P1-08 | Профильные тесты |
| [ModbusTcpProfileTransferServiceTests.cs](../../Configurator.Infrastructure.Modbus.Tests/ModbusTcpProfileTransferServiceTests.cs) | Импорт с настоящим AppConfigService: обе секции либо ни одной | P1-09 | Профильные тесты |
| [MainWindowLifecycleTests.cs](../../Configurator.Tests.RouteMap.Ui/MainWindowLifecycleTests.cs) | Настоящее окно и App handler; подставной активный runtime, повторный Closing | P1-08 | 9 PASS |
| [DialogHostDialogServiceTests.cs](../../Configurator.Tests.RouteMap.Ui/DialogHostDialogServiceTests.cs) | Настоящий DialogHost/Coordinator; результаты, missing host, отмена и посторонняя ошибка | P3-01 | 8 PASS |
| Этот отчёт | Результаты, матрица и ограничения | Все три | Проверка локальных ссылок и diff |

## 3. Атомарное сохранение appsettings.json

Оба пути захватывают существующий `_saveGate` до чтения действующего файла. Внутри gate выполняются чтение, существующие merge/backup операции, подготовка JSON, полная запись в уникальный соседний `.tmp`, проверка cancellation, `File.Move(..., overwrite: true)` и reload. Gate освобождается в `finally`. Следующее сохранение читает уже подтверждённый файл, поэтому два сохранения разных секций не теряют обновления друг друга в рамках одного экземпляра сервиса.

`ReloadConfiguration` вызывается writer-ом после успешного перемещения. До commit сбой записи, cancellation или отказ замены оставляет действующий файл побайтно неизменным; reload не происходит. IOException/UnauthorizedAccessException диагностируются и пробрасываются. Cleanup выполняется best effort; его ошибка логируется, сохраняя первоначальную причину. Повторная запись автоматически не запускается.

Правила сериализации и существующий merge общих секций не менялись. Нетронутые неизвестные секции сохраняются. `PreserveUnknownModbusFields` продолжает сохранять неизвестные поля Modbus и совпадающих записей DataMap/AlarmMap/AddressLabels. Сохраняется регистр существующих имён разделов. Ранее предусмотренный backup `appsettings.json.before-mode-cleanup.<id>.bak` выполняется при прежних условиях удаления obsolete mode mappings; автоматическое удаление backup не добавлено.

При отсутствии общего пользовательского файла создаётся валидный JSON. Тест импорта использует настоящий конфигурационный сервис: `Modbus` и `ModbusDemo` подготовлены в одном временном документе и подтверждаются одной заменой. Runtime не получает новую карту при отказе сохранения.

Это гарантия замены файла в пределах одной файловой системы; отдельная гарантия устойчивости к отключению питания/fsync не добавлялась. Межпроцессная координация также не добавлялась.

## 4. Запуск, настройки окна и завершение

Текст читается с прежним BOM-aware поведением, а исходные байты сохраняются отдельно. Корневой JSON должен быть объектом; десериализация и размеры проверяются до применения к окну. Width/Height должны быть конечными положительными числами. Дублирующиеся свойства, включая вложенные, отклоняются на границе разбора через `JsonDocumentOptions.AllowDuplicateProperties = false`; это предотвращает поздний `ArgumentException` ленивого JsonObject.

При JSON/I/O/access ошибке текущего файла записывается warning с причиной и путём, возвращаются `WindowSettings` из действующей конфигурации. Значения по умолчанию UserSettings 1200×800 используются только при некорректных размерах самих defaults. Загрузка не удаляет и не перезаписывает повреждённый файл. Отсутствующий файл обрабатывается штатно с прежним поиском legacy-файлов.

`SaveUserSettings` сериализует подготовленные значения, сохраняет неизвестные свойства существующего документа, пишет уникальный соседний `.tmp`, затем заменяет основной файл. Если обнаружен повреждённый файл, до замены исходные байты записываются в `user_settings.json.corrupt.<id>.bak`. Ошибка создания копии препятствует перезаписи. При отказе записи или замены действующий файл остаётся неизменным; причина логируется и пробрасывается. Общая runtime-конфигурация при сохранении окна не reload-ится.

Legacy migration оставляет исходник неизменным, сохраняет неизвестные поля и использует атомарный writer окна. Если перенос не удался, считанные legacy-настройки доступны текущей сессии, а причина диагностируется. Блокировка чтения/сохранения окна локальна этому сервису и не меняет Modbus lifecycle.

`MainWindow.OnWindowClosing` ловит только IOException, UnauthorizedAccessException и JsonException вокруг чтения/сохранения настроек. Ошибка логируется; последующие Closing handlers продолжают выполняться. Посторонний InvalidOperationException тестом подтверждён как непроглоченный.

`App.axaml.cs` не изменён: сохраняются экспорт журнала, Dispose, StopModbusRuntimeAsync, `_isShutdownInProgress`, `_isShutdownAllowed` и финальный повторный Close. Headless fixture вызывает настоящий обработчик App, проверяет Dispose провайдера сигналов до StopAsync, ровно одну остановку активного fake runtime за три Closing и фактическое закрытие окна после разрешения stop barrier. Конкретная цепочка зависимостей `MainViewModel.Dispose` в этом узком fixture не создаётся; её вызов в App сохранён без изменений.

## 5. Матрица обязательных регрессий

Во всех строках PASS означает автоматизированную проверку в текущей сессии. Portable-инъекция выполняется через внутренний callback непосредственно перед commit; задержки для моделирования отказов не используются. Тест конкуренции и lifecycle используют сигналы/barrier, а timeouts ограничивают зависание теста.

| ID | Сценарий | Ожидаемый результат | Фактический результат | Статус |
|---|---|---|---|---|
| C01 | SaveSection, отказ перед replace | Старые байты сохранены | AtomicSave_FailureOrCancellationBeforeReplace: single/failure; 0 reload | PASS |
| C02 | SaveSections, отказ перед replace | Старые байты сохранены | Тот же тест: multiple/failure | PASS |
| C03 | Отмена до replace | Старый файл; нет reload | Тот же тест: single и multiple/cancel | PASS |
| C04 | Успех одной секции | Commit до reload | AtomicSave_CommitsAllSectionsBeforeReload, single; callback читает новый файл | PASS |
| C05 | Успех нескольких секций | Согласованный документ | Тот же тест: multiple; обе секции видны при reload | PASS |
| C06 | Неизвестные данные | Секции и Modbus extensions сохранены | Тест commit и существующие tests unknown fields/lowercase/repeated save | PASS |
| C07 | Obsolete mappings | Прежний backup | Backup theory для single/multiple; содержимое backup совпадает с исходником | PASS |
| C08 | Два конкурентных сохранения | Нет потери секции | AtomicSave_ConcurrentSaves_ReadTheLastCommittedFile, управляемый barrier | PASS |
| C09 | Нет общего файла | Создан валидный файл | SaveSectionAsyncCreatesMissingSharedFileAndReloadsConfiguration | PASS |
| C10 | Ошибка самой замены | Старый файл и диагностика | FileMoveDeniedByWindowsHandle: реальный File.Move отвергнут ОС; 0 reload | PASS |
| C11 | Загрузка после отказа | Предыдущий валидный JSON | Failure/cancellation test открывает новый JSON configuration provider | PASS |
| C12 | Импорт Modbus-профиля | Обе секции либо ни одной | Apply_WithRealConfiguration_CommitsBothSectionsOrNeither: success/failure | PASS |
| W01 | Обрезанный JSON | Defaults, нет падения | InvalidFile theory; исходные байты неизменны при Load | PASS |
| W02 | Невалидный JSON/структура | Defaults и диагностика | Текст, array/null, неверный тип, размеры, root/nested duplicates | PASS |
| W03 | Недоступно чтение | Defaults с warning | LockedForReading: FileShare.None; исходник сохранён | PASS |
| W04 | Каталог вместо файла | Обработанный отказ | DirectoryInsteadOfFile: fallback и диагностированный отказ Save | PASS |
| W05 | Повреждение → сохранение | Оригинальные байты доступны | InvalidFile theory: BOM и точные bytes в .corrupt.*.bak; повторный нормальный Save без новой копии | PASS |
| W06 | Отказ записи/replace | Валидный файл не повреждён | ReplacementFailure + реальный FileMoveDeniedByWindowsHandle; temp очищен | PASS |
| W07 | Успех сохранения | Новые параметры читаются | InvalidFile recovery, UserSettingsAreStoredAtExplicitPerUserPath, SuccessPreservesUnknownFields | PASS |
| W08 | Legacy migration | Перенос; исходник сохранён | LegacyUserSettingsAreMigratedToPerUserPath; неизвестное поле и fallback при отказе переноса | PASS |
| W09 | Width/Height/fullscreen | Прежнее поведение | Window_StillAppliesAndSavesSizeAndFullScreen: normal/fullscreen | PASS |
| W10 | Ошибка настроек при Closing | Shutdown продолжен | Closing_SettingsFailure: read/save × IO/access/JSON; последующие handlers вызваны | PASS |
| W11 | Ошибка + активный fake runtime | Dispose и stop выполнены | Тот же lifecycle test: dispose → stop → closed; runtime остановлен | PASS |
| W12 | Повторный Closing | Нет цикла/повторного stop | Тот же test: 3 Closing, 1 Dispose, 1 StopAsync, окно закрыто | PASS |
| D01 | Подтверждение | true | Confirm_ReturnsActualUserResult: оба overload | PASS |
| D02 | Отказ/закрытие | false | Те же theory cases + закрытие host без typed payload | PASS |
| D03 | Нет host | Диагностический InvalidOperationException | MissingHost: точный текст и сохранённое InnerException | PASS |
| D04 | Иная ошибка Coordinator | Не подавлена | OtherCoordinatorException: тот же экземпляр InvalidOperationException; cancellation отдельно | PASS |
| D05 | Компиляция | Нет CS0162 из мёртвой ветки | Перекомпиляция изменённого сервиса и solution build | PASS |

Дополнительно проверены отказ записи временного общего файла и ошибка cleanup, не скрывающая исходное исключение. Windows-тесты реального отказа File.Move помечаются SKIP на других ОС, где нет соответствующей delete-sharing семантики; в этой сессии оба выполнены с PASS.

## 6. Точные команды и результаты

```powershell
git branch --show-current
git rev-parse HEAD
git status --short
git diff --check
dotnet --version

dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~AppConfigServiceTests|FullyQualifiedName~ModbusTcpProfileTransferServiceTests'

dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter 'FullyQualifiedName~MainWindowLifecycleTests'

dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore --filter 'FullyQualifiedName~DialogHostDialogServiceTests'

dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore --no-build

git diff --check
git diff --cached --check
git status --short
```

| Проверка | Результат |
|---|---|
| Конфигурация + импорт, итоговый профильный запуск | **PASS 48/48** |
| Новый lifecycle fixture | **PASS 9/9** |
| Новый dialog fixture | **PASS 8/8** |
| Финальная solution build | **PASS**, 0 errors, 0 warnings в incremental build |
| Полная suite: Configurator.Tests.Unit | **PASS 596/596** |
| Полная suite: Configurator.Infrastructure.Modbus.Tests | **PASS 284/284** |
| Полная suite: Configurator.Infrastructure.OpcUa.Tests | **PASS 35/35**; существующие тесты, без изменения реализации |
| Полная suite: Configurator.Tests.RouteMap.Ui | **PASS 161/161** |
| Полная suite всего | **PASS 1076/1076**, exit 0 |
| Restore | **NOT RUN**: assets имелись, зависимости не менялись |
| Native Windows/ALT Linux UI smoke | **NOT RUN**: отдельная разрешённая нативная smoke-среда не использовалась |
| Реальное убийство процесса во время записи | **NOT RUN**: отдельный crash-process fixture не создавался |

Полная suite выполнена повторно после исправления замечаний reviewer. Отдельные запуски каждого полного тестового проекта после этого не дублировались: все четыре выполнены командой solution test.

Для проверки чувствительности регрессий временно возвращались две исходные прямые записи общего файла. Команда `dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~AtomicSave_FailureOrCancellationBeforeReplace'` дала ожидаемые **4 FAIL/4**, обнаружив обход атомарного writer. Исходные исправления затем восстановлены; профильный запуск снова **48 PASS**, solution build снова PASS. Предварительная попытка такого запуска внутри PowerShell wrapper была прервана без диагностического вывода и не считается результатом проверки.

Замечание о duplicate keys сначала воспроизведено командой `dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore --filter 'FullyQualifiedName~WindowSettings_DuplicateKeysDuringSave'`: **FAIL**, ArgumentException из JsonObject.InitializeDictionary → PreserveMissingProperties → SaveUserSettings. После исправления этот test входит в 48 PASS. Тест `WindowSettings_InvalidFile` до исправления дополнительно показал 2 FAIL/10 на root/nested duplicate keys.

Промежуточные ошибки разработки исправлены: missing using/cast IConfigurationRoot в новых тестах; отсутствие DialogHostStyles в lifecycle fixture; выбор неверного свойства DialogHost при проверке content; неоднозначный overload Observable.Create; предположение об IOException вместо фактически полученного UnauthorizedAccessException в Windows File.Move. Финальных FAIL/BLOCKED нет. Несвязанные существующие предупреждения InputDialog, DialogViewFactory, AuthorizationView и неиспользуемого события unit-теста не исправлялись.

## 7. Субагенты и независимый review

- `implementer`, фактическая роль **GPT-6.1 Sol / medium**: только новый MainWindowLifecycleTests.cs, 9 headless tests. Чужие/общие файлы агент не редактировал.
- `reviewer`, фактическая роль **GPT-6.1 Sol / high**: независимый read-only review и повторная проверка исправлений. Builds/edits не выполнял.
- Reviewer нашёл P1: duplicate keys могли выбросить неперехваченный ArgumentException при SaveUserSettings. Исправлено на границе parsing, добавлены root/nested/direct-save регрессии.
- Reviewer нашёл P2: инъекция ошибки до commit не покрывала реальный отказ File.Move. Добавлены два Windows filesystem tests с запретом delete-sharing и диагностикой.
- Итог reviewer: оба замечания закрыты; новых consequential compatibility defects и actionable findings не выявлено.

Модель основного чата задаётся средой Codex. Переключение основного чата на запрошенную GPT-6.1 Sol не выполнялось и не заявляется. Конфигурация агентов не изменялась. Одновременно работало не более одного субагента; параллельного редактирования общих файлов не было.

## 8. Совместимость, безопасность и ограничения

Формат пользовательских JSON и `RouteMapConfigurationDocument.CurrentSchemaVersion = 22` сохранены. Install-level defaults, пользовательские пути Windows/Linux, Application.WorkMode и Boot-контракт не менялись. Сохранены существующие Modbus/ModbusDemo serialization/merge, obsolete backup и legacy migration. Пути конфигурации/packaging, PLC mappings, endpoints, UnitId, SignalId и протокольная семантика не менялись.

Shutdown App и порядок остановки Modbus не изменены. UI-элементы и размеры по умолчанию не менялись. Публичные конструкторы/интерфейсы сохранены; test seam внутренний. Изменений OPC UA реализации/тестов нет; в общем сервисе диалогов для двух существующих OPC UA call sites удалён только аргумент общей helper-функции, как и для остальных её вызовов.

Все файловые тесты используют отдельные временные каталоги. Lifecycle использует подставной runtime без сетевых соединений. Существующие protocol tests выполнялись штатно на mock/loopback. Live PLC writes, внешние промышленные подключения, installer execution и публикация не выполнялись.

Непроверенными остаются перечисленные native smoke и реальное убийство процесса. Headless fixture не создаёт конкретную цепочку MainViewModel.Dispose; проверяет сохранённый App handler и фактический Dispose провайдера сигналов/остановку runtime. Эти ограничения не представляются как выполненные проверки.

## 9. Git

Реализация и проверка выполнены на исходной ветке без изменения HEAD. До отдельного запроса пользователя commit не выполнялся. Push, merge, reset, clean, установка и публикация не выполнялись.

Первоначальные пользовательские изменения сохранены: `.codex/agents/architect_escalation.toml`, `.codex/agents/implementer.toml`, `.codex/agents/protocol_guard.toml`, `.codex/agents/reviewer.toml`, `.codex/config.toml`; существующий untracked `docs/reports/StabilityAudit_d378a30_2026-11-18.ru.md` не редактировался. Они не относятся к результату этой реализации.

Изменения реализации и новые файлы перечислены в таблице раздела 2. Финальные `git diff --check` и `git diff --cached --check`: **PASS**. Предупреждения Git о будущей нормализации LF→CRLF не являются whitespace errors. Этап 8 завершён; дальнейшие этапы не выполнялись.

## 10. Фиксация завершённого этапа

По последующему запросу пользователя завершённые этапы 6–8 фиксируются одним коммитом: P1-09, P1-08, P3-01, их регрессии и этот отчёт. Исходные изменения `.codex` и untracked-файл аудита исключены из коммита. Новые этапы и push не входят в запрос. Результаты проверок и ограничения из этого отчёта остаются действующими; после них изменена только запись о фиксации этапа.
