# PromFlow.Dispatcher — независимый аудит Route Map bucket control

## 1. Общие сведения

- Репозиторий: `zdanilv/PromFlow.Dispatcher`; ветка: `17-demo-v2`.
- HEAD на момент аудита и до оформления этого отчёта: `b3181818a251de67e68d70823116a783659fbd08`.
- Проверенный диапазон реализации: `765e327..b318181`, три коммита `3e65697`, `8746425`, `b318181`; отдельно проверен незакоммиченный diff этого аудита.
- Дата: 2026-11-05 (Europe/Moscow).
- Оркестратор: текущая сессия Codex; проектная конфигурация `.codex/config.toml` задаёт `gpt-6-sol/high`. Использован независимый субагент `reviewer` (`gpt-6.1-sol/high`), только read-only, первый и один целевой повторный проход.
- Исходное рабочее дерево было чистым. Ни live PLC, ни внешних промышленных endpoints, ни реальных команд, ни изменения физических адресов не было.

## 2. Executive Summary

Четыре функциональных требования реализованы: PowerStandby — read-only индикатор, Start — моментальный Pulse, Stop — Toggle с readback, Auto/Manual скрыты и исключены из активного inventory. Независимый reviewer выявил два P1: старый `Start=Latched` мог оставить бит в `1`, а Stop мог навсегда остаться Pending. Оба исправлены и повторно проверены reviewer. Открытых P0/P1 нет.

Полный набор из 481 теста прошёл вне ограниченного sandbox; финальная сборка прошла с 0 предупреждений и 0 ошибок. Ручная проверка установленного UI на Windows/ALT Linux и испытание с реальным ПР103 не выполнялись. Открыт P2: старые `equip.bucket.selector.on/off` могут сохраняться как неиспользуемые строки пользовательского `Modbus.DataMap`. **Вердикт для программной реализации: ACCEPTED WITH LIMITATIONS.** Разрешением на эксплуатацию с реальным ПР103 этот отчёт не является.

## 3. Проверка четырёх требований

| № | Требование | Статус | Доказательство | Примечание |
|---|---|---|---|---|
| 1 | PowerStandby read-only, Good false/true красный/зелёный, unknown серый | PASS | `EquipmentCardView.axaml:159`, `RouteMapRuntimeMapper.cs:200`, `EquipmentCardViewModelTests.cs:166`, `RouteMapSettingsDialogVisualTests.cs:820` | Нет Command/IsChecked на Border; старый selector скрыт для PowerFeedback. |
| 2 | Start — Button, один bounded Pulse | PASS | `EquipmentCardView.axaml:252`, `EquipmentCardViewModel.cs:525`, `ModbusTcpCommandDispatcher.cs:45`, `RouteMapModbusAdapterTests.cs:48` | Неверный Latched mapping теперь отклоняется до записи. |
| 3 | Stop — ToggleButton, Latched true/false и readback | PASS | `EquipmentCardView.axaml:270`, `EquipmentCardViewModel.cs:291`, `EquipmentCardViewModelTests.cs:51`, `RouteMapSettingsDialogVisualTests.cs:1163` | Несовпавший readback завершается ошибкой через 10 с; без blind retry. |
| 4 | Auto/Manual скрыты, нет активного SignalId/команд | PASS | `TopBarView.axaml`, `TopBarViewModel.cs`, `RouteMapSignalInventory.cs:141`, `RouteMapSignalRuntimeAndMappingTests.cs:220` | Точные mode IDs удалены из defaults; старый пользовательский DataMap очищается при контролируемом Save. |

## 4. Архитектурный аудит

- **UI/VM:** Power — неинтерактивный Border 40×40 с `PowerStandby`; Start/Stop имеют отдельные пути; readback обновляет checked без обратного dispatch. Нажатие/отпускание Start порождает одно действие. Отказы показываются в `CommandErrorMessage`.
- **SignalId:** `PowerFeedback` — `Read/Bool`, Start — `ReadWrite/Bool/Pulse`, Stop — `ReadWrite/Bool/Latched`. Inventory исключает исторические TopBar режимы и selector карточки с PowerFeedback; custom reuse точных mode IDs обнаруживается как конфликт.
- **Modbus:** `SignalWriteRequest.RequiredWriteMode` проверяется диспетчером до `SetAsync`, включая custom SignalId. Pulse использует один true, задержку, bounded попытку false и gate на один SignalId. Существующий register-bit read-modify-write не менялся.
- **Readback:** Power Bad/Stale/offline/missing остаётся unknown. Stop остаётся в Pending до согласованного свежего snapshot либо до timeout с видимой ошибкой. Успех транспортной записи не объявляется фактом запуска механизма.
- **Конфигурация:** schema 17; v16→17 убирает selector bindings у `equip.bucket` и создаёт отдельный PowerFeedback без физического адреса. Исторические TopBar DTO остаются инертными. Сохранение Modbus делает backup при удалении exact mode IDs и сохраняет неизвестные поля других точек.
- **Границы:** Boot/ModbusDemo владеют lifecycle; Route Map не создаёт новый transport и не включает его автоматически. OPC UA, AlarmMap, ModbusDemo.DataMap и адреса оставшихся точек не менялись в диапазоне реализации.

## 5. Независимый аудит reviewer

| ID | Severity | Место | Причина, воспроизведение и последствия | Решение и регрессия |
|---|---|---|---|---|
| R1 | P1, закрыт | `Configurator.Desktop/Workspace/RouteMap/ViewModels/EquipmentCardViewModel.cs:539`; `Configurator.Infrastructure.Modbus/RouteMap/ModbusTcpCommandDispatcher.cs:39` | После миграции Route Map старый user DataMap мог сохранить `Start=Latched`. Одна кнопка Start писала true без false и могла оставить командный бит в `1`. | VM требует Pulse, диспетчер сверяет режим до записи. `RouteMapModbusAdapterTests.cs:30` проверяет `Writes=0` для неверного режима; `EquipmentCardViewModelTests.cs:134` проверяет передачу контракта. |
| R2 | P1, закрыт | `Configurator.Desktop/Workspace/RouteMap/ViewModels/EquipmentCardViewModel.cs:572,603`; `Configurator.Desktop/Workspace/RouteMap/Services/RouteMapRuntimeMapper.cs:267` | При `StopOffFeedback=true` mapper мог удерживать checked=false; после успешного dispatch Stop оставался Pending без конца и блокировал повторное действие. | Ограниченное ожидание 10 с, очистка Pending и видимая ошибка, без повторной записи. `EquipmentCardViewModelTests.cs:148` покрывает несовпавший readback. |
| R3 | P2, открыт | `Configurator.Desktop/Workspace/RouteMap/SignalMapping/RouteMapSignalMappingViewModel.cs:224,268` | Старые exact `equip.bucket.selector.on/off` могут остаться orphan-строками saved DataMap и попасть в экспорт, хотя Power UI их не использует. | Отложено: автоматическое удаление без проверки других активных usages может удалить пользовательскую точку. Нужна отдельная узкая cleanup с backup, проверкой usages и тестом Save/Reload. |

Первый review был read-only и не опирался на отчёты чатов A/B/C как доказательство. Один целевой повторный read-only review подтвердил закрытие R1/R2 и отсутствие новых P0/P1. Reviewer не запускал тесты; результаты ниже получены оркестратором.

## 6. Исправления этого аудита

| Файл | Изменение |
|---|---|
| `Configurator.Application/Services/Signals/SignalWriteRequest.cs` | Необязательный `RequiredWriteMode` при сохранении прежнего трёхаргументного конструктора. |
| `Configurator.Desktop/Workspace/RouteMap/ViewModels/EquipmentCardViewModel.cs` | Start передаёт Pulse, Stop Toggle — Latched; ожидание Stop readback ограничено, ошибка видима. |
| `Configurator.Infrastructure.Modbus/RouteMap/ModbusTcpCommandDispatcher.cs` | Отказ при несовпадении режима до любой Modbus-записи. |
| `Configurator.Tests.Unit/RouteMap/EquipmentCardViewModelTests.cs` | Проверки режима запросов и timeout Stop. |
| `Configurator.Infrastructure.Modbus.Tests/RouteMapModbusAdapterTests.cs` | Неверные custom Start/Stop modes дают отказ и ноль writes. |
| `AgentDocs/ru/02-route-map-guide.ru.md`, `AgentDocs/en/02-route-map-guide.en.md` | Описаны runtime validation и timeout Stop. |

## 7. Матрица приёмки

Команды проверки: `U` = `dotnet test Configurator.Tests.Unit/Configurator.Tests.Unit.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` (249/249); `M` = `dotnet test Configurator.Infrastructure.Modbus.Tests/Configurator.Infrastructure.Modbus.Tests.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` (147/147); `H` = `dotnet test Configurator.Tests.RouteMap.Ui/Configurator.Tests.RouteMap.Ui.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` (50/50). Все три — PASS. Путь теста ниже даёт точное доказательство; PASS относится к программной проверке, не к реальному PLC.

| ID | Проверяемое | Метод/доказательство | Статус | Комментарий |
|---|---|---|---|---|
| PWR-01 | Good false, красный, write=0 | U `EquipmentCardViewModelTests.cs:166`; H `RouteMapSettingsDialogVisualTests.cs:820` | PASS | Цвет `#D10000`. |
| PWR-02 | Good true, зелёный, write=0 | U/H те же тесты | PASS | Цвет `#00D107`. |
| PWR-03 | Missing/Bad/Stale/Offline — unknown | U `RouteMapRuntimeMapperTests.cs:507`, `EquipmentCardViewModelTests.cs:166` | PASS | Нейтральный `#3F474D`. |
| PWR-04 | Click/Space/Enter/hover не пишут | H `RouteMapSettingsDialogVisualTests.cs:820` | PASS | `Assert.Empty(dispatcher.Requests)`. |
| PWR-05 | Enabled=false без selector reset | U `RouteMapRuntimeMapperTests.cs:541`, `EquipmentCardViewModelTests.cs:191` | PASS | Нет запроса записи. |
| STA-01 | Один Start → true→false Pulse | U `EquipmentCardViewModelTests.cs:36`; M `RouteMapModbusAdapterTests.cs:48` | PASS | VM + fake Modbus service; end-to-end PLC не проверен. |
| STA-02 | Повтор не перекрывает Pulse | U `EquipmentCardViewModelTests.cs:73`; M `RouteMapModbusAdapterTests.cs:172` | PASS | Gate на SignalId. |
| STA-03 | Ошибка/timeout/cancel после true → bounded false | M `RouteMapModbusAdapterTests.cs:65,83,102,125,144` | PASS | Попытка сброса ограничена; физический результат неизвестен при разрыве. |
| STA-04 | Отпускание pointer не пишет отдельно | H `RouteMapSettingsDialogVisualTests.cs:1140` | PASS | Один activation. |
| STA-05 | Start при Stop=true не пишет Stop=false | U `EquipmentCardViewModelTests.cs:36` | PASS | Только Start SignalId. |
| STP-01 | Checked Stop → true | U `EquipmentCardViewModelTests.cs:51`; H `RouteMapSettingsDialogVisualTests.cs:1163` | PASS | Только Stop SignalId. |
| STP-02 | Unchecked Stop → false | U/H те же тесты | PASS | Не пишет Start. |
| STP-03 | Внешний readback меняет UI без write | U `EquipmentCardViewModelTests.cs:13,51`; H `RouteMapSettingsDialogVisualTests.cs:1163` | PASS | Checked не оптимистичен. |
| STP-04 | Ошибка write/confirmation видима | U `EquipmentCardViewModelTests.cs:115,148`; M `RouteMapModbusAdapterTests.cs:30` | PASS | Pending не остаётся навсегда. |
| TOP-01 | user/admin TopBar без mode и gap | H `RouteMapSettingsDialogVisualTests.cs:1054,1235` | PASS | Ручной platform smoke — NOT RUN. |
| TOP-02 | mode нет в active inventory | U `RouteMapSignalRuntimeAndMappingTests.cs:220` | PASS | Legacy DTO инертны. |
| TOP-03 | exact mode cleanup, backup, другие точки сохранены | U `RouteMapSignalRuntimeAndMappingTests.cs:1019,1093,1138`; M `AppConfigServiceTests.cs:82` | PASS | Очистка запускается контролируемым Save. |
| TOP-04 | Reset/Emergency сохранены | U `RouteMapConfigurationTests.cs:1198,1220,1439`; H `RouteMapSettingsDialogVisualTests.cs:1054` | PASS | Существующие семантики. |
| CFG-01 | v16→17, selector инертен | U `RouteMapConfigurationTests.cs:186` | PASS | Идемпотентная миграция. |
| CFG-02 | schema 1–15 | U `RouteMapConfigurationTests.cs:68,113,147,256,275,309,371,919,953,1036,1075` | PASS | Проверены цепочки/представительные версии; не каждый возможный пользовательский JSON. |
| CFG-03 | JSON round-trip новых типов и данных | U `RouteMapConfigurationTests.cs:27,186,671` | PASS | Стили, параметры, bindings. |
| CFG-04 | duplicate/rename, Power SignalId | U `RouteMapConfigurationTests.cs:484,577` | PASS | Не восстанавливает bucket selector. |
| CFG-05 | Start=Latched/Stop=Pulse отклоняются | U `EquipmentCardViewModelTests.cs:134`; M `RouteMapModbusAdapterTests.cs:30` | PASS | Отказ до записи, включая custom IDs. |
| SAF-01 | Нет связи — нет команды Power; quality/error | U `EquipmentCardViewModelTests.cs:166`, `RouteMapRuntimeMapperTests.cs:507` | PASS | Mock; реальный ПР103 — NOT RUN. |
| SAF-02 | Второй writer того же HR | Review: `AgentDocs/ru/03-modbus-tcp-guide.ru.md`, раздел Register-bit write; этот отчёт, §10 | PASS | Ограничение явно указано; совместная запись не тестировалась. |

## 8. Сборка и точные результаты команд

| Команда | Код | Результат |
|---|---:|---|
| `dotnet test Configurator.Tests.Unit/Configurator.Tests.Unit.csproj --no-restore -v:q` | 1 | FAIL среды: AvaloniaStatsTask не смог читать `%LOCALAPPDATA%/AvaloniaUI/Licensing/Tickets/v2` в sandbox. |
| `dotnet test Configurator.Tests.Unit/Configurator.Tests.Unit.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` | 0 | PASS 249/249 после исправлений; при первой сборке сообщались предупреждения в других файлах. |
| `dotnet test Configurator.Infrastructure.Modbus.Tests/Configurator.Infrastructure.Modbus.Tests.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` | 0 | PASS 147/147 после исправлений. |
| `dotnet test Configurator.Tests.RouteMap.Ui/Configurator.Tests.RouteMap.Ui.csproj --no-restore -p:UsedAvaloniaProducts= -v:q` | 0 | PASS 50/50 до исправлений; полный solution test повторил эти 50 после исправлений. |
| `dotnet test DesktopTemplate.slnx --no-restore -p:UsedAvaloniaProducts= -v:q` (sandbox) | 1 | Unit 249, Modbus 147, UI 50 PASS; OPC UA 7/35 FAIL из-за `FindPackageAssembly("ReactiveUI")` → null в ограниченном NuGet-кэше. |
| `dotnet test Configurator.Infrastructure.OpcUa.Tests/Configurator.Infrastructure.OpcUa.Tests.csproj --no-restore -p:UsedAvaloniaProducts= --filter 'FullyQualifiedName~OpcUaTagRow_ShouldExposeErrorState' --logger 'console;verbosity=detailed'` | 1 | Та же ошибка `ArgumentNullException(assemblyPath)` в `OpcUaViewModelTests.cs:311`. |
| `dotnet test DesktopTemplate.slnx --no-restore -p:UsedAvaloniaProducts= -v:q` (с доступом к локальному NuGet-кэшу) | 0 | PASS: Unit 249, Modbus 147, UI 50, OPC UA 35 = 481/481. |
| `dotnet build DesktopTemplate.slnx --no-restore -p:UsedAvaloniaProducts= -v:q` | 0 | PASS после исправлений: 0 warnings, 0 errors. |
| `python tools/docs/test_check_markdown_links.py` | 0 | PASS 4/4 после уточнений документации и создания отчёта. |
| `git diff --check` | 0 | PASS после создания отчёта; только предупреждения Git о будущей нормализации LF/CRLF. |

`-p:UsedAvaloniaProducts=` отключает только задачу статистики Avalonia, требующую доступ к каталогу лицензирования. Restore не выполнялся: assets уже существовали, зависимости не менялись. Предупреждения при первом профильном тесте относились к `AuthorizationView`, `InputDialogViewModel`, `DialogHostDialogService`, `DialogViewFactory` и существующему stub-event; финальный build показал 0 предупреждений.

## 9. Совместимость

Старый Route Map JSON проходит migration до schema 17; настройки карточки, параметры, геометрия и палитра проверены round-trip тестами. Legacy TopBar поля читаются/сохраняются, но UI и активный inventory их не используют. `Modbus.DataMap` очищается от точных `system.mode.automatic/manual` при контролируемом Save; backup предшествует удалению, другие mappings, AlarmMap и неизвестные поля сохраняются. Import старого Modbus-профиля с exact mode IDs получает диагностический отказ; экспорт такой карты требует очистки. Повторный Save не воссоздаёт mode IDs. Старый Start=Latched/Stop=Pulse теперь отклоняется в runtime с видимой ошибкой до записи; оператор должен исправить mapping. Старые selector orphan-строки остаются открытым P2.

## 10. Промышленная безопасность

UI посылает запросы, PLC остаётся последней инстанцией блокировок и безопасного состояния. PowerFeedback не отправляет команд. Start Pulse имеет один true и ограниченную попытку false даже после неопределённого ответа true; это не гарантирует сброс при потере сети. Нет автоматического повтора после timeout/reconnect. Stop учитывает хороший свежий readback, а неподтверждённое состояние показывает как ошибку. Ошибка транспорта и отсутствие подтверждения не называются исполнением PLC-команды. `СТОП` — обычная программная команда, не аварийный останов.

Register-bit read-modify-write сериализован внутри этого приложения, но **не атомарен относительно второго writer**. Одновременная запись ПК и ПР103 в тот же Holding Register может потерять соседние биты; разделение владельцев регистра требует проверки PLC-контракта. Реальное значение `PulseDurationMs=300`, PLC watchdog/fail-safe и адрес `equip.bucket.power` требуют подтверждения на разрешённом стенде. Live PLC не подключался.

## 11. Остаточные риски

- P0: открытых нет.
- P1: открытых нет; два замечания reviewer закрыты целевой проверкой.
- P2: orphan `equip.bucket.selector.on/off` в старом пользовательском DataMap (R3); возможна гонка старта Modbus-роли во время cleanup mode IDs, при которой runtime требует перезапуска роли (из отчёта этапа C, подтверждено кодом сохранения).
- Внешние ограничения: физический PowerFeedback не сопоставлен без проверенного PLC-контракта; 300 мс Pulse и fail-safe PLC не испытаны; второй writer Holding Register не исключён; ручной smoke установленного UI на Windows/ALT Linux не выполнен. Период опроса больше 10 с может привести к видимой ошибке Stop confirmation до следующего readback — оператор должен сверить состояние по PLC перед новой командой.

## 12. Итоговая приёмка

| Gate | Статус | Основание |
|---|---|---|
| 0 | PASS | Ветка/HEAD и чистое исходное дерево подтверждены, контракты Power/Start/Stop и владельцы найдены. |
| 1 | PASS | Schema 17, seed, migration, inventory, validator и round-trip тесты. |
| 2 | PASS | Start Pulse и Stop Toggle проверены unit/Modbus/headless; оба P1 закрыты. |
| 3 | PASS | Power Border 40×40, readback/unknown, отсутствие writes при input — headless PASS. |
| 4 | PASS | Mode UI/commands/inventory/defaults инертны; exact mode cleanup с backup и диагностикой. |
| 5 | PASS | 481/481 тестов, полный build, RU/EN docs и markdown links; platform smoke ограничен. |
| 6 | PASS | Независимый reviewer, исправление двух P1, один целевой повторный review, отчёт. |

**ACCEPTED WITH LIMITATIONS** — программная реализация принята по mock/headless/local тестам; эксплуатационная готовность с реальным ПР103 не подтверждена и не разрешается этим отчётом.

## 13. Необходимые действия

1. Перед стендом проверить PLC-контракт физического PowerFeedback, длительности Start Pulse, fail-safe сброса и независимых блокировок.
2. На разрешённом стенде проверить Start/Stop/readback, потерю связи и второго writer; выполнить ручной smoke Windows/ALT Linux.
3. Отдельно согласовать адресную очистку orphan selector mappings с проверкой usages и backup; до неё не считать старый экспорт полностью очищенным от legacy строк.
