# Archive Operations Guide

This guide describes the archive subsystem after Stage 14. Archive code lives behind
Application contracts and Persistence infrastructure. Desktop and Avalonia code may call
archive services, but archive storage, partitioning, query, export, backup, retention and
health logic must not depend on Avalonia, ReactiveUI or ViewModels.

## Runtime Ownership

Archive startup and shutdown are centralized by `ApplicationRuntimeCoordinator`.
Workspace view models do not start the archive runtime or the Modbus archive collector.
The startup order is persistence initialization, installation identity, license refresh,
archive runtime start, collector subscription and the existing Modbus autostart policy.
Shutdown disposes workspace state, stops Modbus, unsubscribes the collector, flushes and
stops archive runtime, then releases the single-instance lease.

## Health And Evidence

Use `IArchiveHealthService.Observe()` and `ArchiveHealth` snapshots as the primary
operator evidence. The important fields are `State`, `QueueDepth`,
`DroppedTelemetryCount`, `LastSuccessAtUtc`, `LastErrorCode` and `LastErrorMessage`.
High queue depth or growing dropped telemetry means archive storage is not keeping up,
but emergency command delivery must remain available.

## Query And UI Rules

Archive UI is paged and cancelable. It must not load full history into memory. Snapshot
lists use metadata queries first; raw coil/register values are decoded only when the user
opens details for one selected snapshot. Security audit queries require
`Permission.ViewSecurityAudit` as well as the `Archive` license feature.

## Production Configuration Sample

The application does not ship an `Archive` section by default. Production deployments can
add one using the existing `ArchiveOptions` shape:

```json
{
  "Archive": {
    "Enabled": true,
    "DatabaseDirectory": "%LOCALAPPDATA%\\PromFlow.Dispatcher\\archive",
    "ExportDirectory": "%LOCALAPPDATA%\\PromFlow.Dispatcher\\exports",
    "BatchSize": 100,
    "FlushIntervalMs": 1000,
    "ChannelCapacity": 10000,
    "QueryMaxPageSize": 500,
    "ExportMaxRangeDays": 31,
    "ExportMaxRecords": 100000
  }
}
```

Keep storage local or on a fast, reliable volume. Do not place SQLite partitions on an
unreliable network share unless the operational risk is accepted and tested.

## Failure Handling

Archive unavailability is degraded service, not a reason to block emergency delivery.
Retention, export and backup failures return typed operation results. Corrupted old
partitions should fail the affected query with a typed archive failure while later valid
partitions remain queryable.
