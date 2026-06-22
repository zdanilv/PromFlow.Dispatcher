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
