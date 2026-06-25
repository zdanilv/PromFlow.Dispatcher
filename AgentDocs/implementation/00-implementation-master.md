# PromFlow.Dispatcher — полный исполняемый план для Codex

> Репозиторий: `zdanilv/PromFlow.Dispatcher`  
> Базовая ветка: `6-add-archive`  
> Стек: C#/.NET 10, Avalonia 12, ReactiveUI, DI, Modbus TCP  
> Дата: 22.06.2026

## 0. Правила исполнения

Этот документ нельзя выполнять целиком за один запуск. Один этап — одна изолированная задача, один проверяемый diff и отдельный commit.

Перед каждым этапом Codex обязан прочитать:

1. `AGENTS.md`;
2. `AgentDocs/en/00-master.en.md`;
3. этот документ;
4. `AgentDocs/implementation/PROGRESS.md`;
5. файлы, перечисленные в конкретном этапе.

Codex обязан сначала выполнить read-only анализ и назвать предполагаемые файлы. Нельзя начинать следующий этап до сборки, тестов, review и обновления progress.

## 1. Цель

Реализовать три подсистемы:

1. Архив сырых Modbus snapshot, команд, физических записей, readback, состояний связи и ошибок.
2. Локальные учётные записи с ролями `User`/`Administrator` и permission-based authorization.
3. Офлайн-лицензию `*.promlicense`, подписанную ECDSA, с профилем заказчика, сроком, версией и features.

## 2. Исходные ограничения проекта

- RouteMap работает через `SignalId`, не через физические адреса.
- `Modbus.DataMap` и `ModbusDemo.DataMap` не смешиваются.
- `ModbusDemo` сохраняет владение endpoint/lifecycle общего runtime.
- Полный сырой архив берётся из `IModbusRuntimeService.SnapshotChanged` и `ModbusSnapshot`.
- `IModbusDataSnapshotSource` содержит только декодированные точки DataMap и не заменяет raw archive.
- Архиватор не зависит от Avalonia/ViewModel.
- SQL не выполняется в Modbus callback.
- UI не является safety layer; interlocks и окончательное принятие команды остаются в PLC.
- UI visibility не является authorization.
- Лицензия и роль пользователя — независимые условия доступа.
- Закрытый ключ лицензии отсутствует в production app и GitHub.
- Пароль администратора не hardcoded; встроенной является системная роль и bootstrap flow.

## 3. Целевая цепочка

```text
Avalonia UI
  -> Authentication/session
  -> IAccessDecisionService
       -> user permission
       -> optional license feature
       -> runtime preconditions
  -> Authorized command decorator
  -> Command audit decorator
  -> ModbusTcpCommandDispatcher
  -> IModbusTcpService
  -> PLC readback

IModbusRuntimeService
  -> ModbusArchiveCollector
  -> bounded priority buffer
  -> single SqliteArchiveWriter
  -> monthly SQLite/WAL partitions
```

## 4. Новые production/test проекты

```text
Configurator.Infrastructure.Persistence/
  Common/
  Archive/
  Security/
  Licensing/

Configurator.Infrastructure.Persistence.Tests/
  Archive/
  Security/
  Licensing/
  TestSupport/

tools/PromFlow.LicenseIssuer/
```

`Configurator.Infrastructure.Persistence` зависит от Application, но не от Desktop/Avalonia/ReactiveUI. `PromFlow.LicenseIssuer` не является reference рабочего приложения.

## 5. Ветки

Рекомендуемая цепочка:

```text
6-add-archive
  -> 7-archive-contracts
  -> 8-archive-sqlite
  -> 9-archive-runtime
  -> 10-command-audit
  -> 11-archive-operations
  -> 12-auth-foundation
  -> 13-auth-ui-rbac
  -> 14-license-core
  -> 15-license-ui-policy
  -> 16-lifecycle-hardening
```

Допустима одна feature-ветка, но каждый этап остаётся отдельным атомарным commit.

---

# Этап 0. Baseline и agent context

## Цель

Зафиксировать исходное состояние без изменения production-поведения.

## Прочитать

- `AgentDocs/en/00-master.en.md`;
- `01-architecture-overview.en.md`;
- `03-modbus-tcp-guide.en.md`;
- `05-coding-rules.en.md`;
- `06-testing-and-diagnostics.en.md`;
- `Configurator.Boot/Program.cs`;
- `Configurator.Desktop/App.axaml.cs`;
- `MainViewModel`, `WorkspaceViewModel`, `WorkspaceView.axaml`;
- Modbus DI.

## Шаги

1. Выполнить:
   ```powershell
   git status --short
   git rev-parse HEAD
   dotnet restore .\DesktopTemplate.slnx
   dotnet build .\DesktopTemplate.slnx --no-restore
   dotnet test .\DesktopTemplate.slnx --no-restore
   ```
2. Записать baseline SHA, warnings, failing tests и окружение.
3. Добавить:
   ```text
   AGENTS.md
   AgentDocs/implementation/00-implementation-master.md
   AgentDocs/implementation/PROGRESS.md
   AgentDocs/implementation/DECISIONS.ru.md
   ```
4. Добавить ссылки в master docs.
5. Не исправлять старые failures в этом этапе; только документировать.
6. Повторить build.

## Готово, когда

- production-код не изменён;
- документы находятся в Git;
- `AGENTS.md` короткий и практичный;
- baseline воспроизводим;
- progress содержит точный commit.

Commit: `docs: add implementation plan and Codex guidance`

---

# Этап 1. Application contracts архива

## Цель

Определить типы и interfaces без SQLite/runtime/UI.

## Добавить

```text
Configurator.Application/Services/Archiving/
  ArchiveOptions.cs
  ArchiveHealth.cs
  ArchivePriority.cs
  ArchiveEnvelope.cs
  RawModbusSnapshotArchiveRecord.cs
  ModbusStatusArchiveRecord.cs
  EquipmentCommandAuditRecord.cs
  PhysicalModbusWriteAuditRecord.cs
  SecurityAuditRecord.cs
  ArchiveQuery.cs
  ArchivePage.cs
  IArchiveIngestor.cs
  IArchiveQueryService.cs
  IArchiveMaintenanceService.cs
  ICommandAuditService.cs
  IArchiveHealthService.cs
  IArchiveRuntime.cs
```

## Raw snapshot record

Минимальные поля:

```csharp
Guid Id
string DeviceId
ModbusRuntimeRole Role
long SequenceNumber
DateTimeOffset CapturedAtUtc
int CoilStartAddress
int HoldingRegisterStartAddress
IReadOnlyList<bool> Coils
IReadOnlyList<ushort> HoldingRegisters
string ConfigurationHash
ArchiveResolution Resolution
int SchemaVersion
```

Требования:

- immutable;
- UTC normalization;
- defensive copy arrays;
- stable logical `DeviceId`, не только IP;
- sequence monotonic per role/process;
- никакой зависимости от SQLite.

## Archive priority

```text
Critical  — command/security terminal records
Normal    — runtime/configuration events
Telemetry — raw snapshot
```

## Options

```text
Enabled
BaseDirectory
DeviceId
HighResolutionRetentionHours
LongTermRetentionDays
LongTermSnapshotIntervalMs
ChannelCapacity
BatchSize
BatchFlushIntervalMs
BusyTimeoutMs
PartitionMode
ExportDirectory
```

Добавить validation и tests для invalid interval/capacity/path/device ID.

## Не делать

- не ставить SQLite package;
- не менять Modbus runtime;
- не менять DI/UI.

## Готово, когда

- Application не зависит от Infrastructure;
- contracts документированы;
- validation покрыта unit tests;
- solution собирается.

Commit: `feat(archive): add application archive contracts`

---

# Этап 2. SQLite foundation и миграции

## Цель

Создать отдельное локальное хранилище с явными миграциями и WAL.

## Шаги

1. Создать production/test projects net10.0.
2. Добавить их в `DesktopTemplate.slnx`.
3. Persistence -> Application; Tests -> Persistence/Application.
4. Добавить `Microsoft.Data.Sqlite`, закрепив совместимую stable-версию.
5. Реализовать:
   ```text
   IAppDataPathProvider
   SqliteConnectionFactory
   SqlitePragmaInitializer
   SqliteMigrationRunner
   PersistenceHealthState
   ```
6. Не писать БД в installation directory.
7. Применять к connection:
   ```sql
   PRAGMA foreign_keys = ON;
   PRAGMA busy_timeout = <option>;
   PRAGMA journal_mode = WAL;
   PRAGMA synchronous = FULL;
   ```
8. Создать migration ledger:
   ```sql
   CREATE TABLE IF NOT EXISTS schema_migration (
       version INTEGER PRIMARY KEY,
       name TEXT NOT NULL,
       applied_at_utc_ms INTEGER NOT NULL,
       checksum TEXT NOT NULL
   );
   ```
9. Миграции выполняются ordered, idempotent и transactionally.
10. Для WAL tests использовать file-backed temporary DB, не только `:memory:`.

## Первая схема

```sql
CREATE TABLE archive_partition_metadata (
    id INTEGER PRIMARY KEY CHECK (id = 1),
    archive_schema_version INTEGER NOT NULL,
    created_at_utc_ms INTEGER NOT NULL,
    device_id TEXT NOT NULL,
    application_version TEXT NOT NULL
);

CREATE TABLE modbus_snapshot (
    id TEXT PRIMARY KEY,
    device_id TEXT NOT NULL,
    runtime_role INTEGER NOT NULL,
    captured_at_utc_ms INTEGER NOT NULL,
    sequence_number INTEGER NOT NULL,
    resolution_class INTEGER NOT NULL,
    coil_start_address INTEGER NOT NULL,
    holding_register_start_address INTEGER NOT NULL,
    coil_count INTEGER NOT NULL,
    holding_register_count INTEGER NOT NULL,
    coils_blob BLOB NOT NULL,
    holding_registers_blob BLOB NOT NULL,
    configuration_hash TEXT NOT NULL,
    archive_schema_version INTEGER NOT NULL,
    created_at_utc_ms INTEGER NOT NULL
);

CREATE UNIQUE INDEX ux_modbus_snapshot_sequence
ON modbus_snapshot(device_id, runtime_role, sequence_number, resolution_class);

CREATE INDEX ix_modbus_snapshot_time
ON modbus_snapshot(device_id, captured_at_utc_ms);

CREATE TABLE runtime_event (
    id TEXT PRIMARY KEY,
    occurred_at_utc_ms INTEGER NOT NULL,
    device_id TEXT,
    event_type TEXT NOT NULL,
    severity INTEGER NOT NULL,
    message TEXT NOT NULL,
    details_json TEXT
);

CREATE INDEX ix_runtime_event_time
ON runtime_event(occurred_at_utc_ms);
```

## Tests

- first/repeated initialize;
- checksum mismatch;
- rollback on migration error;
- concurrent initialization;
- PRAGMA values;
- invalid directory;
- expected indexes/schema version.

## Готово, когда

- Persistence не зависит от Desktop;
- migration repeatable;
- WAL verified on file DB;
- tests/build pass.

Commit: `feat(persistence): add SQLite foundation and migration runner`

---

# Этап 3. Binary snapshot codec и monthly partitions

## Цель

Хранить один snapshot одной строкой и BLOB, а не строкой на каждый регистр.

## Codec

Coils:

- 8 bool на byte;
- первый coil — bit 0;
- count хранится отдельно и проверяется decoder.

Registers:

- 2 bytes на `ushort`;
- byte order фиксирован и задокументирован;
- decoder не зависит от CPU endianness.

Header:

```text
Magic: PFS1
CodecVersion: UInt16
ItemType: UInt8
Reserved: UInt8
ItemCount: Int32
Payload
```

Запрещён `BinaryFormatter`.

## Partition resolver

Имя:

```text
promflow-{sanitizedDeviceId}-{yyyy-MM}.sqlite
```

Требования:

- boundary по UTC;
- безопасный sanitized ID;
- writer закрывает старое connection при смене месяца;
- query умеет выбирать несколько partitions;
- current partition writable, старые read-only при query.

## Tests

- coils count 0,1,7,8,9,1000;
- all false/true/alternating;
- registers 0/1/65535;
- deterministic round-trip;
- bad magic/version/truncated/count mismatch;
- month/year boundary.

## Готово, когда

- round-trip без потерь;
- corruption отклоняется typed error;
- формат документирован;
- нет runtime integration.

Commit: `feat(archive): add snapshot binary codec and monthly partitions`

---

# Этап 4. Prioritized bounded writer

## Цель

Создать single-writer pipeline, не блокирующий Modbus callback.

## Компоненты

```text
ArchiveIngestor
ArchivePriorityBuffer
SqliteArchiveWriter
ArchiveRuntime
ArchiveHealthService
```

## Buffer policy

Нельзя использовать один безусловный `DropOldest`, который способен потерять command/security record.

Рекомендуемо:

```text
Critical bounded channel — no silent loss
Normal bounded channel — bounded wait
Telemetry latest-slot/channel — coalesce/drop telemetry only
```

`TryEnqueueTelemetry` должен быть быстрым и без SQL. Critical enqueue имеет cancellation/timeout и observable failure.

## Batch

Flush при:

- `BatchSize`;
- `BatchFlushIntervalMs`;
- stop;
- partition change.

Один batch = одна SQL transaction.

## Failure policy

1. health -> degraded/faulted;
2. structured log;
3. bounded exponential backoff;
4. no tight loop;
5. telemetry допускает контролируемую потерю с counter;
6. critical record не теряется молча;
7. Emergency command не блокируется archive failure.

## Lifecycle

```csharp
Task StartAsync(CancellationToken)
Task FlushAsync(CancellationToken)
Task StopAsync(CancellationToken)
ValueTask DisposeAsync()
```

Start/Stop idempotent.

## Tests

- size/timer/stop flush;
- cancellation;
- locked DB and recovery;
- telemetry overload counter;
- critical no-silent-loss;
- partition change;
- writer exception не попадает в UI thread;
- health transitions;
- no unobserved task.

## Готово, когда

- enqueue path не выполняет SQL;
- memory bounded;
- shutdown deterministic;
- tests/build pass.

Commit: `feat(archive): add prioritized buffered SQLite writer`

---

# Этап 5. ModbusArchiveCollector

## Цель

Подписать archive pipeline на полный сырой runtime, не на UI и не только DataMap.

## Точка подписки

```text
IModbusRuntimeService.SnapshotChanged
IModbusRuntimeService.StatusChanged
```

Размещение:

```text
Configurator.Infrastructure.Modbus/Archiving/
  ModbusArchiveCollector.cs
  ModbusConfigurationFingerprint.cs
```

## Callback snapshot

1. Проверить `Archive.Enabled`.
2. Принять Client/Server role.
3. Скопировать coils/registers в новые arrays.
4. Выдать sequence.
5. Нормализовать UTC.
6. Получить start addresses из current options.
7. Получить cached configuration hash.
8. Создать immutable record.
9. Быстро enqueue telemetry.
10. Вернуться.

Запрещены SQL, UI dispatch, долгий `await`, сериализация огромного JSON и mutation runtime snapshot.

## Configuration fingerprint

Canonical data:

```text
role, endpoint, port, unitId, start addresses, counts,
polling options, DataMap name/area/address/length/bit/type/access/write mode
```

Hash: SHA-256 lowercase hex. Порядок DataMap deterministic.

## Sampling

- HighResolution: каждый runtime snapshot, retention 24–72 часа.
- LongTerm: не чаще одного раза в 1000 ms, retention 30–90 дней.

Status event сохраняется только при meaningful transition, а не каждый цикл.

## DI/lifecycle

Collector singleton, но его создание не означает запуск. Подписка/отписка выполняется централизованным lifecycle.

## Tests

- defensive copy;
- role/start address;
- monotonic sequence;
- stable/change-sensitive hash;
- status duplicate suppression;
- high/long-term sampling;
- unsubscribe;
- no Avalonia dependency.

## Готово, когда

- полный raw snapshot архивируется;
- callback не блокируется БД;
- существующие Modbus tests проходят.

Commit: `feat(modbus): archive raw snapshots and runtime transitions`

---

# Этап 6. Audit команд и физических Modbus writes

## Цель

Хранить намерение оператора и точный payload, реально отправленный устройству.

## Semantic command

Поля:

```text
CommandId, CorrelationId, RequestedAtUtc, CompletedAtUtc,
SessionId, UserId, Username, DeviceId,
SignalId, ValueType, RequestedValueCanonical, WriteMode,
Result, ErrorCode, ErrorMessage,
ConfirmationStatus, ConfirmedAtUtc
```

## Physical write

```text
WriteId, CommandId, AttemptedAtUtc, CompletedAtUtc,
Role, Area, Address, Quantity, PayloadBlob,
Succeeded, ErrorCode, ErrorMessage
```

## Correlation

Создать явный immutable `CommandExecutionContext`; не использовать static mutable ambient variable.

## Pipeline

```text
access check
 -> CommandRequested audit
 -> actual dispatcher
 -> physical write observer
 -> success/failure
 -> readback terminal status
```

Попытка должна сохраняться даже при validation failure.

## Pulse

Одна semantic command, две physical writes (`true`, затем `false`) с одним `CommandId`.

## Confirmation statuses

```text
NotApplicable, Pending, Confirmed, TimedOut, ConnectionLost, Rejected
```

`Succeeded write` не означает `Confirmed` без readback.

## Schema migration

Добавить `equipment_command` и `modbus_write`, foreign key и индексы по времени, user, signal, result, command ID.

## Failure policy

Audit failure не должен блокировать Emergency. Для обычных команд возможен configurable fail-closed, но default и rationale должны быть отражены ADR.

## Tests

- success;
- missing/read-only/type mismatch;
- Modbus stopped;
- network exception;
- timeout;
- pulse two writes;
- cancellation during pulse;
- archive failure + emergency;
- concurrent commands;
- terminal result always present.

## Готово, когда

- любая попытка имеет terminal result;
- physical writes коррелированы;
- Modbus semantics/readback не ухудшены.

Commit: `feat(audit): persist equipment commands and physical Modbus writes`

---

# Этап 7. Query, export, retention, backup

## Цель

Дать production operations без загрузки всей истории в память.

## Query

Фильтры:

- UTC date range;
- device/role;
- event type;
- signal/user/result;
- paging/order;
- hard maximum page size.

Query service выбирает нужные monthly partitions. Cancellation обязателен.

## Export

Форматы:

- CSV: commands/events;
- NDJSON: diagnostics/snapshot metadata;
- ZIP package:
  ```text
  manifest.json
  commands.csv
  events.csv
  snapshots.ndjson
  checksums.sha256
  ```

Писать в temp, затем atomic rename. Корректно escaping CSV/JSON и ограничивать размер/диапазон.

## Retention

- high resolution старше hours;
- long term старше days;
- command/security retention отдельно;
- active partition не удаляется;
- старый monthly file лучше удалять целиком после проверки;
- retention action audit-ится.

## Backup

Использовать SQLite backup API/согласованный WAL-aware метод. Не копировать один `.sqlite` при активном WAL.

Manifest содержит UTC, app/schema version, device, partitions, SHA-256.

Поддержать `PRAGMA quick_check`; полный integrity check — только maintenance/diagnostics.

## Tests

- paging/multi-partition;
- cancellation;
- export escaping/temp cleanup;
- retention boundary/current protection;
- backup during write;
- open/restore backup;
- corrupted partition reported.

## Готово, когда

- query/export memory bounded;
- backup consistent;
- retention observable.

Commit: `feat(archive): add querying export retention and backup`

---

# Этап 8. Учётные записи и сессии — foundation

## Цель

Заменить демонстрационную `IAuthApp/AuthApp` production-моделью без включения UI flow.

## Типы

```text
UserRole
Permission
AppUser
UserSession
UserSessionSnapshot
AuthenticationRequest/Result
AuthorizationDecision
PasswordPolicy
UserLockoutPolicy
```

Роли:

```text
User
Administrator
```

Permissions:

```text
ViewRouteMap
IssueEquipmentCommands
ViewArchive
ExportArchive
ViewSignalMapping
EditSignalMapping
ViewModbusDiagnostics
ConfigureModbus
ManageUsers
InstallLicense
ViewLicense
ViewSecurityAudit
RunArchiveMaintenance
```

## Interfaces

```text
IAuthenticationService
IAuthorizationService
IUserRepository
IUserSessionAccessor
IUserManagementService
ISecurityAuditService
IPasswordHashService
```

## Passwords

Использовать adapter над `PasswordHasher<AppUser>`. Запрещены plain, reversible encryption, SHA-256(password), settings/license password.

## User schema

```sql
CREATE TABLE app_user (
    id TEXT PRIMARY KEY,
    username TEXT NOT NULL,
    normalized_username TEXT NOT NULL UNIQUE,
    password_hash TEXT NOT NULL,
    role INTEGER NOT NULL,
    is_enabled INTEGER NOT NULL,
    failed_login_count INTEGER NOT NULL,
    lockout_until_utc_ms INTEGER,
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL,
    password_changed_at_utc_ms INTEGER NOT NULL,
    last_login_at_utc_ms INTEGER,
    row_version INTEGER NOT NULL
);

CREATE TABLE security_audit (
    id TEXT PRIMARY KEY,
    occurred_at_utc_ms INTEGER NOT NULL,
    event_type TEXT NOT NULL,
    severity INTEGER NOT NULL,
    actor_user_id TEXT,
    actor_username TEXT,
    session_id TEXT,
    target_user_id TEXT,
    result INTEGER NOT NULL,
    reason_code TEXT,
    details_json TEXT
);
```

## Policy

- invariant username normalization;
- generic invalid-credentials response;
- configurable lockout (default 5/15 min);
- audit success/failure/locked/disabled;
- session only after success;
- signout destroys current session;
- thread-safe state.

## Bootstrap admin

Встроена системная роль, но не пароль. При zero users:

1. bootstrap required;
2. пользователь задаёт strong password;
3. первый Administrator создаётся transactionally;
4. повторный bootstrap forbidden;
5. событие audit.

## Tests

- hash/verify;
- wrong/unknown/disabled;
- lockout/expiry/reset;
- session/signout;
- permission matrix;
- bootstrap once;
- normalized duplicate;
- row version conflict.

## Готово, когда

- hardcoded credentials не участвуют в production path;
- Application contracts не зависят от UI;
- persistence/tests готовы.

Commit: `feat(auth): add local users sessions and permission model`

---

# Этап 9. Login flow, dynamic workspace и enforcement

## Цель

Включить production login и запретить доступ к admin-функциям на UI и service boundary.

## Startup route

```text
initialize persistence/license/user state
 -> AdminBootstrapView if zero users
 -> AuthorizationView
 -> Workspace after success
```

Отсутствие лицензии не блокирует Administrator login.

## AuthorizationViewModel

- async command, не `async void`;
- username/password binding;
- повторный submit disabled;
- cancellation/lifetime;
- generic error;
- password не логируется и очищается;
- navigation only after success.

## Dynamic tabs

Заменить статические TabItems на descriptors/factories:

```text
Id, Header, RequiredPermission, RequiredLicenseFeature?, Factory, Order
```

User создаёт только `Route Map`. Administrator получает разрешённые Archive/Signal Mapping/Modbus Demo/Users/License/Diagnostics.

## Service guards

Защитить:

- `IEquipmentCommandDispatcher`;
- RouteMap configuration mutation;
- Modbus configuration mutation;
- archive export/maintenance;
- user management;
- license installation.

## Access formula

```text
Authenticated AND Permission AND OptionalLicenseFeature AND RuntimePreconditions
```

PLC acceptance — следующая независимая граница.

## Recovery without license

Administrator может login, install/view license, diagnostics, users, archive read/export и configuration согласно policy. Admin role не даёт автоматический обход `RemoteControl` feature.

## Tests

- login/default route;
- zero users bootstrap;
- User only Route Map;
- Admin tabs;
- direct service call denied;
- no session denied;
- expired/missing license admin recovery;
- logout disposes workspace.

## Готово, когда

- старая hardcoded auth не используется;
- User не получает admin ViewModels;
- guards покрыты tests;
- RouteMap работает после login.

Commit: `feat(auth): enable login flow RBAC and protected workspace`

---

# Этап 10. Offline license core и issuer

## Цель

Создать tamper-evident license format и отдельный offline issuer.

## Cryptography

```text
ECDSA P-256
SHA-256
DSASignatureFormat.IeeeP1363FixedFieldConcatenation
```

Подписываются exact UTF-8 payload bytes.

## Envelope

```json
{
  "format": "PromFlow.License",
  "schemaVersion": 1,
  "algorithm": "ES256",
  "keyId": "prod-2026-01",
  "payload": "BASE64URL",
  "signature": "BASE64URL"
}
```

## Payload

```json
{
  "licenseId": "uuid",
  "product": "PromFlow.Dispatcher",
  "issuedAtUtc": "2026-06-22T10:00:00Z",
  "validFromUtc": "2026-06-22T00:00:00Z",
  "expiresAtUtc": "2027-06-22T23:59:59Z",
  "edition": "Professional",
  "licenseVersion": 1,
  "productVersion": { "minimum": "1.0.0", "maximumExclusive": "2.0.0" },
  "features": ["RouteMap", "RemoteControl", "Archive", "ArchiveExport"],
  "customer": { "fullName": "...", "phone": "...", "email": "..." },
  "organization": { "name": "...", "siteAddress": "..." },
  "installation": { "bindingMode": "InstallationId", "installationId": "..." }
}
```

## Contracts

```text
LicenseFeature/Edition/Envelope/Profile/State/Status
LicenseValidationError
ILicenseService
ILicenseVerifier
ILicenseStore
IInstallationIdentityService
ITrustedTimeStateStore
```

## Validation order

1. file size;
2. UTF-8/JSON;
3. envelope format/schema/algorithm;
4. known key ID;
5. Base64Url;
6. signature;
7. payload parse;
8. product;
9. semantic fields;
10. dates;
11. product version;
12. installation binding;
13. clock rollback;
14. features/edition consistency.

## Keys

Production app содержит только trusted public keys keyed by `keyId`. Production private key не создаётся и не commit-ится.

## Issuer CLI

```text
promflow-license generate-key
promflow-license issue --profile ... --private-key ...
promflow-license verify --license ...
promflow-license inspect --license ...
```

Test key явно test-only и rejected production verifier.

## Installation identity

Random 256-bit ID, создаётся один раз и экспортируется в `*.promrequest`. Не использовать только MAC/disk serial.

## Clock rollback

Хранить `MaxObservedUtc`, skew и typed `ClockRollbackDetected`. Полностью непробиваемая offline-защита невозможна; limitation документируется. Platform secure-state adapters вводятся через interface.

## PII

Подпись не шифрует профиль. Не логировать полный payload. Шифрование payload — отдельный scope.

## Tests

- valid;
- payload mutation/bad signature/unknown key;
- schema/algorithm;
- expired/not-yet-valid;
- wrong product/version/installation;
- malformed/oversized;
- clock rollback;
- key rotation;
- production rejects test key.

## Готово, когда

- offline verify deterministic;
- production private key отсутствует;
- validation errors typed;
- issuer отделён.

Commit: `feat(license): add signed offline license format and issuer`

---

# Этап 11. License installation, UI и feature policy

## Цель

Дать Administrator безопасную установку/замену license и связать features с access decision.

## Storage/install

Current file:

```text
<AppData>/license/current.promlicense
```

Алгоритм:

1. выбрать файл;
2. size-limit read;
3. полная validation до записи;
4. temp write + flush;
5. atomic replace;
6. metadata/security audit;
7. immutable state update;
8. notification UI.

Invalid license не заменяет valid current.

## License statuses

```text
Missing, Valid, Expired, NotYetValid, InvalidSignature,
InvalidFormat, WrongInstallation, WrongProductVersion,
ClockRollback, StorageError
```

## Admin UI

Показывать status, edition, dates, organisation/site/customer, version range, features, installation request/export и reason code. Не показывать private key и не логировать signature/profile целиком.

## Feature mapping

```text
Route Map view -> RouteMap
Commands -> RemoteControl
Archive view -> Archive
Archive export -> ArchiveExport
Signal mapping -> EngineeringTools
Modbus Demo -> Diagnostics/EngineeringTools
```

Role и feature проверяются независимо.

## Audit

Installation attempt/success/failure, previous/new license ID, actor, UTC, reason; без полного PII payload.

## Tests

- atomic replace;
- invalid does not replace valid;
- state event;
- access changes;
- User denied missing license;
- Admin recovery;
- picker cancellation/storage error.

## Готово, когда

- приложение восстанавливается после missing/invalid license;
- Administrator не становится bypass коммерческих features;
- state thread-safe/immutable.

Commit: `feat(license): add installation UI and feature enforcement`

---

# Этап 12. Archive UI

## Цель

Добавить admin-oriented paged UI без полной загрузки БД.

## Views/ViewModels

```text
ArchiveView
ArchiveViewModel
ArchiveFilterViewModel
ArchiveSnapshotDetailsViewModel
CommandAuditViewModel
RuntimeEventsViewModel
SecurityAuditViewModel
ArchiveMaintenanceViewModel
```

Разделы:

```text
Snapshots | Commands | Events | Security Audit | Maintenance
```

## Требования

- server-side paging;
- local date input -> UTC query;
- cancellation предыдущего запроса;
- max page size;
- virtualized DataGrid;
- snapshot BLOB decode только по details request;
- address-range slice;
- decimal/hex/bit view;
- export progress;
- archive health;
- maintenance permissions;
- ошибка архива не блокирует RouteMap.

## Tests

- timezone conversion;
- cancellation/paging;
- permission;
- query error;
- no UI-thread blocking;
- basic headless visuals.

## Готово, когда

- opening Archive не загружает всю историю;
- User не создаёт/видит Archive UI;
- operations cancelable.

Commit: `feat(ui): add paged archive and audit workspace`

---

# Этап 13. Централизованный lifecycle

## Цель

Гарантировать порядок startup/shutdown и archive flush.

## Coordinator

```csharp
public interface IApplicationRuntimeCoordinator
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
```

## Startup

```text
1. App-data paths.
2. Persistence migrations.
3. Installation identity.
4. Installed license validation.
5. User repository/bootstrap decision.
6. Archive writer start.
7. Modbus collector subscribe.
8. Existing Modbus autostart policy.
9. Login/bootstrap UI.
```

## Shutdown

```text
1. Mark shutting down.
2. Reject new non-emergency commands.
3. Dispose workspace subscriptions.
4. Stop Modbus runtime.
5. Unsubscribe collector.
6. Flush archive.
7. WAL checkpoint/close DB.
8. Dispose session/license services.
9. Allow window close.
```

Timeouts bounded; exception одного subsystem не предотвращает controlled cleanup остальных. Нет `.Wait/.Result` на UI thread.

## Single instance

SQLite writer предполагает один процесс. Добавить OS mutex/lock file и typed handling second instance.

## Tests

- start/stop twice;
- migration/archive/Modbus failure;
- pending snapshot flush;
- pulse during shutdown;
- timeout/exception aggregation;
- repeated window close.

## Готово, когда

- fire-and-forget lifecycle устранён/наблюдаем;
- old manual shutdown делегирует coordinator;
- deterministic close подтверждён.

Commit: `refactor(runtime): centralize startup and graceful shutdown`

---

# Этап 14. Hardening и production acceptance

## Цель

Не добавлять features; проверить отказоустойчивость, безопасность и эксплуатацию.

## Нагрузочные/аварийные сценарии

1. 100/250/500/1000 ms polling.
2. 24-hour simulated load.
3. burst 10x.
4. slow/full/locked disk.
5. process kill during transaction.
6. month boundary.
7. clock rollback.
8. license expiry during session.
9. disabled user during session.
10. connection loss during command.
11. pulse interrupted by shutdown.
12. large export.
13. corrupted old partition.
14. backup during writes.

## Метрики

```text
SnapshotsReceived/Queued/Persisted/DroppedTelemetry
CriticalRecordsQueued/Failed
ArchiveQueueDepth/BatchDuration/LastSuccess/DatabaseSize
CommandsRequested/Succeeded/Failed/TimedOut
LicenseStatus
AuthenticationFailures
```

Не логировать passwords, production key material и полный license PII.

## Security review

Проверить credentials/private keys/debug bypass, missing guards, path traversal, oversized input, SQL injection, symlink issues, tampered DB, clock rollback и license copy.

## Code review

Cancellation, async disposal, no UI blocking, bounded collections, no SQL in callback, no `async void` commands, UTC, deterministic serialization, migrations/tests/docs.

## Документация

Добавить EN/RU guides:

```text
07-archive-guide
08-authorization-guide
09-offline-license-guide
10-operations-and-recovery
```

## Final verification

```powershell
dotnet restore .\DesktopTemplate.slnx
dotnet build .\DesktopTemplate.slnx --no-restore
dotnet test .\DesktopTemplate.slnx --no-restore

dotnet test .\Configurator.Infrastructure.Modbus.Tests\Configurator.Infrastructure.Modbus.Tests.csproj --no-restore
dotnet test .\Configurator.Infrastructure.Persistence.Tests\Configurator.Infrastructure.Persistence.Tests.csproj --no-restore
dotnet test .\Configurator.Tests.Unit\Configurator.Tests.Unit.csproj --no-restore
dotnet test .\Configurator.Tests.RouteMap.Ui\Configurator.Tests.RouteMap.Ui.csproj --no-restore
```

## Готово, когда

- нет неизвестных failing tests;
- critical scenarios имеют evidence;
- recovery/release documented;
- safety остаётся в PLC;
- final diff reviewed.

Commit: `test: harden archive auth and offline licensing workflows`

---

# 6. Рекомендуемые configuration sections

```json
{
  "Archive": {
    "Enabled": true,
    "DeviceId": "concrete-distributor-01",
    "BaseDirectory": "",
    "HighResolutionRetentionHours": 72,
    "LongTermRetentionDays": 90,
    "LongTermSnapshotIntervalMs": 1000,
    "ChannelCapacity": 10000,
    "BatchSize": 250,
    "BatchFlushIntervalMs": 200,
    "BusyTimeoutMs": 5000,
    "PartitionMode": "Monthly"
  },
  "Authentication": {
    "MaxFailedAttempts": 5,
    "LockoutMinutes": 15,
    "MinimumPasswordLength": 12,
    "RequireDigit": true,
    "RequireUppercase": true,
    "RequireLowercase": true
  },
  "Licensing": {
    "Product": "PromFlow.Dispatcher",
    "LicenseFileName": "current.promlicense",
    "AllowedClockSkewMinutes": 5,
    "BindingMode": "InstallationId"
  }
}
```

Не хранить password/private key/bootstrap secret в settings.

# 7. Definition of Done каждого этапа

- [ ] Scope соблюдён.
- [ ] Нет незапрошенного широкого refactor.
- [ ] Build проходит.
- [ ] Релевантные tests проходят.
- [ ] Новое поведение покрыто tests.
- [ ] Нет secrets/debug bypass.
- [ ] Нет UI-thread blocking.
- [ ] Cancellation/lifecycle обработаны.
- [ ] DI lifetime обоснован.
- [ ] Migration idempotent при schema change.
- [ ] `PROGRESS.md` обновлён.
- [ ] Diff review выполнен.
- [ ] Commit message указан.
- [ ] Следующий этап не начат.

# 8. Не входит в первый релиз

- online activation/license server;
- cloud historian/PostgreSQL/TimescaleDB;
- Active Directory/OAuth/JWT для локального desktop;
- изменение PLC safety logic;
- microservices;
- ECDSA signature каждого snapshot;
- SQL row на каждый регистр;
- полное шифрование архива;
- HSM на клиентском объекте без отдельного требования.

# 9. Решения владельца до production

1. Windows-only или full cross-platform production.
2. Machine-wide/per-user storage.
3. Точный retention.
4. Fail-open/fail-closed ordinary commands при audit failure.
5. Emergency audit policy.
6. Admin recovery capabilities без license.
7. Editions/features.
8. Portable/bound license.
9. Где хранится production private key.
10. PII/encryption policy license file.
11. Clock rollback recovery.
12. Backup destination/operator responsibility.
13. Защита от локального OS administrator.
14. RTO/RPO при повреждении БД.

# 10. Универсальный prompt этапа

```text
Работай в PromFlow.Dispatcher на ветке, происходящей от 6-add-archive.

Прочитай:
- AGENTS.md
- AgentDocs/en/00-master.en.md
- AgentDocs/implementation/00-implementation-master.md
- AgentDocs/implementation/PROGRESS.md
- файлы, перечисленные в этапе <N>.

Выполни только этап <N>: <название>.

Перед изменением кода:
1. проверь git status и активную ветку;
2. изучи текущую реализацию и tests;
3. перечисли proposed files/contracts/migrations/tests/risks;
4. сообщи о противоречиях;
5. не начинай другие этапы.

Сохрани Modbus/RouteMap invariants и PLC safety boundary.
Не добавляй secrets и scope creep.
Добавь tests, запусти проверки и обнови PROGRESS.md.
В конце выдай files, commands, results, limitations и commit message.
```

# 11. Короткие stage prompts

## Stage 0

```text
Выполни только этап 0. Это documentation/baseline; production-код не менять.
Существующие failures только документировать.
```

## Stage 1

```text
Выполни только этап 1. Добавь immutable Application archive contracts/validation/tests.
SQLite/runtime/DI/UI не менять.
```

## Stage 2

```text
Выполни только этап 2. Создай Persistence production/test projects, explicit SQLite migrations и WAL tests.
Не подключай Modbus/UI.
```

## Stage 3

```text
Выполни только этап 3. Versioned deterministic binary codec и UTC monthly partitioning.
Добавь corruption/boundary tests; runtime не подключать.
```

## Stage 4

```text
Выполни только этап 4. Bounded priority writer, no SQL in enqueue, critical no-silent-loss,
telemetry overload policy и flush-on-stop tests.
```

## Stage 5

```text
Выполни только этап 5. Полный источник — IModbusRuntimeService.SnapshotChanged.
No UI/SQL in callback; existing Modbus behavior сохранить.
```

## Stage 6

```text
Выполни только этап 6. Semantic command + physical writes + readback terminal status.
Pulse = одна command и две writes. Emergency не блокировать archive failure.
```

## Stage 7

```text
Выполни только этап 7. Paged/cancelable query, safe export, retention и WAL-aware backup.
Полноценный Archive UI не добавлять.
```

## Stage 8

```text
Выполни только этап 8. Production users/sessions/permissions/password hashing/lockout/bootstrap contracts.
UI flow оставить следующему этапу.
```

## Stage 9

```text
Выполни только этап 9. Login/bootstrap/dynamic tabs/service guards.
User только Route Map; visibility не считать authorization.
```

## Stage 10

```text
Выполни только этап 10. ECDSA P-256/SHA-256 exact-payload offline license и separate issuer.
Production private key не создавать и не commit-ить.
```

## Stage 11

```text
Выполни только этап 11. Atomic license install, admin recovery UI и feature enforcement.
Invalid не заменяет valid; admin role не bypass features.
```

## Stage 12

```text
Выполни только этап 12. Paged/virtualized/cancelable Archive UI с admin permissions.
Не загружай полный архив.
```

## Stage 13

```text
Выполни только этап 13. Central startup/shutdown coordinator, single instance и archive flush.
Старую ручную shutdown-логику делегировать без смены Modbus ownership.
```

## Stage 14

```text
Выполни только этап 14. Load/security/recovery tests и production docs.
Новых features не добавлять.
```

# 12. Review prompt после этапа

```text
Не начинай следующий этап.
Проведи review текущего diff относительно AGENTS.md и acceptance criteria этапа <N>.
Сначала перечисли findings по severity с файлами/строками.
Проверь scope, dependencies, security, concurrency, cancellation, lifecycle,
SQLite/WAL, authorization bypass, key handling, Modbus semantics, PLC safety и tests.
Исправь только подтверждённые проблемы, затем повтори tests и обнови PROGRESS.md.
```

# 13. Формат handoff

```markdown
## Stage completion report

### Stage
<N> — <name>

### Summary
...

### Files added/modified
- ...

### Architecture decisions
- ...

### Commands executed
```powershell
...
```

### Test results
- Build:
- Unit:
- Integration:
- UI:

### Known limitations
- ...

### Security notes
- ...

### Git status
...

### Recommended commit
`...`

### Next stage
<N+1> — <name>
```
