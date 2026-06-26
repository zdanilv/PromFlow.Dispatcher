# Built-In SQLite Database Guide

`PromFlow.Dispatcher` stores users, security audit and archive records in SQLite files.
The normal operator path is the application UI and archive services. Direct database
inspection is for diagnostics, support and controlled recovery.

Prefer read-only access. Stop the application before any direct write.

## Tools

- `sqlite3` CLI for repeatable commands.
- DB Browser for SQLite for GUI inspection.
- PowerShell for copying database sidecar files and locating partitions.

Open a database read-only:

```powershell
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Security\promflow-security.sqlite"
```

List tables:

```sql
.tables
```

Run an integrity check:

```sql
PRAGMA integrity_check;
```

## File Locations

Security database default:

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

Archive base directory default:

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

Archive partition names:

```text
promflow-{sanitizedDeviceId}-{yyyy-MM}.sqlite
```

For example:

```text
promflow-plant-01-2026-07.sqlite
```

Export directory default:

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

## Safe Backup And Edit Workflow

1. Stop `PromFlow.Dispatcher`.
2. Copy the `.sqlite` file and sidecars if present: `.sqlite-wal` and `.sqlite-shm`.
3. Open the copied database first and run `PRAGMA integrity_check;`.
4. Use read-only mode for diagnostics.
5. If a controlled repair is unavoidable, run it in a transaction:

```sql
BEGIN IMMEDIATE;
-- controlled repair statement here
PRAGMA integrity_check;
COMMIT;
```

If the check fails, use `ROLLBACK;` and restore from the copied files.

## Edit Policy

Supported direct operations:

- Inspect/query records.
- Copy databases for backup.
- Use emergency lab repair after a full backup.

Prefer the application UI or services for:

- User creation, disable/enable and password changes.
- License installation.
- Archive export, backup and retention.

Do not hand-edit these fields unless this is a controlled recovery with a backup:

- `password_hash`
- raw snapshot BLOB columns
- audit history rows
- primary keys
- `row_version`
- `archive_partition_metadata`

## Table Map

Security database:

| Table | Purpose |
|---|---|
| `app_user` | Local users, roles, enabled state, lockout and row version |

Archive partitions:

| Table | Purpose |
|---|---|
| `archive_partition_metadata` | Schema version, device id and application version for one partition |
| `modbus_snapshot` | Raw Modbus snapshot metadata and BLOB values |
| `runtime_event` | Archive/runtime status and operational events |
| `equipment_command` | Logical equipment command audit records |
| `modbus_write` | Physical Modbus write audit records |
| `security_audit` | Authentication, authorization, license and user-management audit records |

## Security Database Queries

Inspect users without password hashes:

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

Find disabled users:

```sql
SELECT username, role, row_version
FROM app_user
WHERE is_enabled = 0
ORDER BY normalized_username;
```

Do not query or export `password_hash` for ordinary diagnostics.

## Locate Archive Partitions

PowerShell:

```powershell
Get-ChildItem -Path "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive" -Filter "promflow-*.sqlite" | Sort-Object Name
```

Open a partition read-only:

```powershell
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive\promflow-plant-01-2026-07.sqlite"
```

Inspect partition metadata:

```sql
SELECT
  archive_schema_version,
  device_id,
  application_version,
  datetime(created_at_utc_ms / 1000, 'unixepoch') AS created_at_utc
FROM archive_partition_metadata;
```

## Modbus Snapshot Metadata

Query latest snapshot metadata without reading BLOB columns:

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

Filter by time range:

```sql
SELECT id, captured_at_utc_ms, sequence_number, coil_count, holding_register_count
FROM modbus_snapshot
WHERE captured_at_utc_ms BETWEEN unixepoch('2026-07-01T00:00:00Z') * 1000
                            AND unixepoch('2026-07-02T00:00:00Z') * 1000
ORDER BY captured_at_utc_ms ASC
LIMIT 100;
```

Get one snapshot details row by `id` and `captured_at_utc_ms`:

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

Export BLOB values as hex files from PowerShell:

```powershell
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive\promflow-plant-01-2026-07.sqlite" "SELECT hex(coils_blob) FROM modbus_snapshot WHERE id = 'paste-snapshot-guid';" > .\coils_blob.hex
sqlite3 -readonly "$env:LOCALAPPDATA\PromFlow.Dispatcher\Archive\promflow-plant-01-2026-07.sqlite" "SELECT hex(holding_registers_blob) FROM modbus_snapshot WHERE id = 'paste-snapshot-guid';" > .\holding_registers_blob.hex
```

## Snapshot BLOB Decode Rules

Both BLOBs use the same 12-byte header:

| Offset | Size | Meaning |
|---:|---:|---|
| `0` | `4` | ASCII magic `PFS1` |
| `4` | `2` | UInt16 little-endian codec version, currently `1` |
| `6` | `1` | Item type: `1` coils, `2` holding registers |
| `7` | `1` | Reserved, must be `0` |
| `8` | `4` | Int32 little-endian item count |

Coils are bit-packed after the header. Bit `index % 8` in byte `index / 8` is the coil
value. The Modbus address is:

```text
coil_start_address + index
```

Holding registers are UInt16 little-endian values after the header. The byte offset for
register index `n` is:

```text
12 + (n * 2)
```

The Modbus address is:

```text
holding_register_start_address + n
```

Use the Archive UI for normal snapshot details. Use direct BLOB decode only when you are
diagnosing storage or recovering evidence outside the application.

## Archive Export Path

Archive UI export creates a bounded package. The package includes:

- `commands.csv`
- `modbus_writes.csv`
- `events.csv`
- `snapshots.ndjson`

`snapshots.ndjson` contains snapshot metadata: IDs, addresses, counts, configuration hash
and timestamps. It does not contain decoded coil/register arrays. Use the Archive UI
details or direct BLOB decoding for values.
