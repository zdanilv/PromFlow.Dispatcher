# Руководство по встроенным SQLite базам

`PromFlow.Dispatcher` хранит пользователей, security audit и archive records в SQLite
файлах. Обычный путь оператора - UI приложения и archive services. Прямое открытие базы
нужно для диагностики, support и controlled recovery.

Предпочитайте read-only access. Перед прямой записью остановите приложение.

## Инструменты

- `sqlite3` CLI для повторяемых команд.
- DB Browser for SQLite для GUI inspection.
- PowerShell для копирования database sidecar files и поиска partitions.

Открыть базу read-only:

```powershell
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Security\promflow-security.sqlite"
```

Показать таблицы:

```sql
.tables
```

Проверить целостность:

```sql
PRAGMA integrity_check;
```

## Расположение файлов

Security database по умолчанию:

```text
%LOCALAPPDATA%\PromFlow.Dispatcher\Security\promflow-security.sqlite
```

Override:

Configuration key: `Authentication.SecurityDatabasePath`.

```json
{
  "Authentication": {
    "SecurityDatabasePath": "D:\\PromFlow\\Security\\promflow-security.sqlite"
  }
}
```

Archive base directory по умолчанию:

```text
%LOCALAPPDATA%\PromFlow.Dispatcher\Archive
```

Archive override:

Configuration key: `Archive.BaseDirectory`.

```json
{
  "Archive": {
    "Enabled": true,
    "BaseDirectory": "D:\\PromFlow\\Archive",
    "DeviceId": "plant-01"
  }
}
```

Имена archive partitions:

```text
promflow-{sanitizedDeviceId}-{yyyy-MM}.sqlite
```

Например:

```text
promflow-plant-01-2026-07.sqlite
```

Export directory по умолчанию:

```text
<ArchiveBaseDirectory>\Exports
```

Override:

Configuration key: `Archive.ExportDirectory`.

```json
{
  "Archive": {
    "ExportDirectory": "D:\\PromFlow\\Exports"
  }
}
```

## Safe backup и edit workflow

1. Остановите `PromFlow.Dispatcher`.
2. Скопируйте `.sqlite` file и sidecars, если они есть: `.sqlite-wal` и `.sqlite-shm`.
3. Сначала откройте копию базы и выполните `PRAGMA integrity_check;`.
4. Для диагностики используйте read-only mode.
5. Если controlled repair неизбежен, выполняйте его в transaction:

```sql
BEGIN IMMEDIATE;
-- controlled repair statement here
PRAGMA integrity_check;
COMMIT;
```

Если check не прошел, выполните `ROLLBACK;` и восстановите файлы из копии.

## Политика редактирования

Поддержанные direct operations:

- Inspect/query records.
- Copy databases for backup.
- Emergency lab repair после полного backup.

Предпочитайте UI приложения или services для:

- создания пользователей, enable/disable и смены password;
- установки license;
- archive export, backup и retention.

Не редактируйте вручную эти поля без controlled recovery и backup:

- `password_hash`
- raw snapshot BLOB columns
- audit history rows
- primary keys
- `row_version`
- `archive_partition_metadata`

## Карта таблиц

Security database:

| Table | Назначение |
|---|---|
| `app_user` | Local users, roles, enabled state, lockout и row version |

Archive partitions:

| Table | Назначение |
|---|---|
| `archive_partition_metadata` | Schema version, device id и application version для partition |
| `modbus_snapshot` | Raw Modbus snapshot metadata и BLOB values |
| `runtime_event` | Archive/runtime status и operational events |
| `equipment_command` | Logical equipment command audit records |
| `modbus_write` | Physical Modbus write audit records |
| `security_audit` | Authentication, authorization, license и user-management audit records |

## Запросы к Security database

Посмотреть users без password hashes:

```sql
SELECT
  id,
  username,
  role,
  is_enabled,
  failed_login_count,
  datetime(lockout_until_utc_ms / 1000, 'unixepoch') AS lockout_until_utc,
  datetime(created_at_utc_ms / 1000, 'unixepoch') AS created_at_utc,
  datetime(updated_at_utc_ms / 1000, 'unixepoch') AS updated_at_utc,
  datetime(last_login_at_utc_ms / 1000, 'unixepoch') AS last_login_at_utc,
  row_version
FROM app_user
ORDER BY normalized_username;
```

Найти disabled users:

```sql
SELECT username, role, row_version
FROM app_user
WHERE is_enabled = 0
ORDER BY normalized_username;
```

Для обычной диагностики не запрашивайте и не экспортируйте `password_hash`.

## Поиск Archive partitions

PowerShell:

```powershell
Get-ChildItem -Path "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive" -Filter "promflow-*.sqlite" | Sort-Object Name
```

Открыть partition read-only:

```powershell
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive\promflow-plant-01-2026-07.sqlite"
```

Посмотреть partition metadata:

```sql
SELECT
  archive_schema_version,
  device_id,
  application_version,
  datetime(created_at_utc_ms / 1000, 'unixepoch') AS created_at_utc
FROM archive_partition_metadata;
```

## Modbus snapshot metadata

Запросить последние snapshot metadata без BLOB columns:

```sql
SELECT
  id,
  device_id,
  runtime_role,
  datetime(captured_at_utc_ms / 1000, 'unixepoch') AS captured_at_utc,
  captured_at_utc_ms,
  sequence_number,
  resolution_class,
  coil_start_address,
  coil_count,
  holding_register_start_address,
  holding_register_count,
  configuration_hash,
  archive_schema_version
FROM modbus_snapshot
ORDER BY captured_at_utc_ms DESC, sequence_number DESC
LIMIT 20;
```

Фильтр по времени:

```sql
SELECT id, captured_at_utc_ms, sequence_number, coil_count, holding_register_count
FROM modbus_snapshot
WHERE captured_at_utc_ms BETWEEN unixepoch('2026-07-01T00:00:00Z') * 1000
                            AND unixepoch('2026-07-02T00:00:00Z') * 1000
ORDER BY captured_at_utc_ms ASC
LIMIT 100;
```

Получить details row по `id` и `captured_at_utc_ms`:

```sql
SELECT
  id,
  device_id,
  runtime_role,
  captured_at_utc_ms,
  sequence_number,
  coil_start_address,
  holding_register_start_address,
  hex(coils_blob) AS coils_blob_hex,
  hex(holding_registers_blob) AS holding_registers_blob_hex
FROM modbus_snapshot
WHERE id = 'paste-snapshot-guid'
  AND captured_at_utc_ms = 1782864000000
LIMIT 1;
```

Экспортировать BLOB values как hex files из PowerShell:

```powershell
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive\promflow-plant-01-2026-07.sqlite" "SELECT hex(coils_blob) FROM modbus_snapshot WHERE id = 'paste-snapshot-guid';" > .\coils_blob.hex
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive\promflow-plant-01-2026-07.sqlite" "SELECT hex(holding_registers_blob) FROM modbus_snapshot WHERE id = 'paste-snapshot-guid';" > .\holding_registers_blob.hex
```

## Snapshot BLOB decode rules

Оба BLOB используют одинаковый 12-byte header:

| Offset | Size | Meaning |
|---:|---:|---|
| `0` | `4` | ASCII magic `PFS1` |
| `4` | `2` | UInt16 little-endian codec version, сейчас `1` |
| `6` | `1` | Item type: `1` coils, `2` holding registers |
| `7` | `1` | Reserved, должен быть `0` |
| `8` | `4` | Int32 little-endian item count |

Coils bit-packed после header. Bit `index % 8` в byte `index / 8` - значение coil.
Modbus address:

```text
coil_start_address + index
```

Holding registers - UInt16 little-endian values после header. Byte offset для register
index `n`:

```text
12 + (n * 2)
```

Modbus address:

```text
holding_register_start_address + n
```

Для обычных snapshot details используйте Archive UI. Direct BLOB decode нужен только для
диагностики storage или recovery evidence вне приложения.

## Archive export path

Archive UI export создает bounded package. Package включает:

- `commands.csv`
- `modbus_writes.csv`
- `events.csv`
- `snapshots.ndjson`

`snapshots.ndjson` содержит snapshot metadata: IDs, addresses, counts, configuration hash
и timestamps. Он не содержит decoded coil/register arrays. Для values используйте Archive
UI details или direct BLOB decoding.
