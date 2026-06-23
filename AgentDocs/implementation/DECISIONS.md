# PromFlow.Dispatcher — implementation decisions

Use this file for short Architecture Decision Records.

---

## ADR-001 — SQLite for local archive storage

- Status: Proposed
- Date: 2026-06-22

### Context

PromFlow.Dispatcher must store high-frequency Modbus snapshots, command audit records,
security events, and support local offline query/export without requiring a server.

### Decision

Use local SQLite databases with WAL, explicit migrations, a single buffered writer,
and monthly partitions. Store complete raw coil/register snapshots as versioned BLOB values.
Store commands and searchable events in normalized tables.

### Consequences

- The application remains offline and deployable as a desktop product.
- Writes must be serialized through one writer.
- Backup must be WAL-aware.
- JSON remains an export/configuration format, not the primary historian database.

---

## ADR-002 — Separate role permissions from license features

- Status: Proposed
- Date: 2026-06-22

### Context

A local user role describes who is operating the application. A license describes what the
installed product copy is commercially allowed to do.

### Decision

Require both permission and license feature for licensed operations. Administrator recovery
capabilities remain available without a user license, but administrator role does not bypass
commercial feature checks.

### Consequences

- UI and services need a shared access decision service.
- Missing/expired licenses do not prevent administrator recovery.
- User and license state must be tested independently.

---

## ADR-003 — ECDSA-signed offline license envelopes

- Status: Proposed
- Date: 2026-06-22

### Context

Licenses must be issued offline as files and must resist customer-side editing.

### Decision

Use ECDSA P-256 with SHA-256. The issuer signs exact UTF-8 payload bytes. The production
application embeds only trusted public keys. Production private keys never enter the repo.

### Consequences

- License files are tamper-evident but not confidential.
- Key rotation is handled by `keyId`.
- A separate issuer tool and test-only key material are required.

---

## ADR-004 - Command audit failure policy

- Status: Proposed
- Date: 2026-06-23

### Context

RouteMap commands must be auditable as semantic operator intent and as the exact physical
Modbus writes sent through `IModbusTcpService.SetAsync`. Archive infrastructure can be
temporarily unavailable, but emergency delivery must not be delayed by archive failures.

### Decision

Command audit uses an explicit `CommandExecutionContext` passed from
`ModbusTcpCommandDispatcher` into `IModbusTcpService.SetAsync`; no ambient static context is
used. `CommandAuditFailureMode.FailOpen` is the default. `FailClosed` may block ordinary
commands only when the initial `CommandRequested` audit cannot be accepted, before the first
Modbus write. Signals listed in `ArchiveOptions.EmergencySignalIds` always fail open.
Physical Modbus write audit is best-effort, bounded by `CommandAuditEnqueueTimeoutMs`, and
never retries or changes an already attempted PLC write result.

### Consequences

- Emergency commands remain deliverable when archive storage is unavailable.
- Operators can configure fail-closed semantics for ordinary commands where audit capture is
  mandatory.
- Pulse commands use one `CommandId` across set and reset physical writes.
- Persistence remains behind `ICommandAuditService`; Modbus code has no SQLite dependency.
