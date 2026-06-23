CREATE TABLE equipment_command (
    command_id TEXT PRIMARY KEY,
    correlation_id TEXT NOT NULL,
    requested_at_utc_ms INTEGER NOT NULL,
    completed_at_utc_ms INTEGER,
    session_id TEXT,
    user_id TEXT,
    username TEXT,
    device_id TEXT NOT NULL,
    signal_id TEXT NOT NULL,
    value_type INTEGER NOT NULL,
    requested_value_canonical TEXT NOT NULL,
    write_mode INTEGER,
    result INTEGER NOT NULL,
    error_code TEXT,
    error_message TEXT,
    confirmation_status INTEGER NOT NULL,
    confirmed_at_utc_ms INTEGER,
    archive_schema_version INTEGER NOT NULL,
    created_at_utc_ms INTEGER NOT NULL,
    updated_at_utc_ms INTEGER NOT NULL
);

CREATE TABLE modbus_write (
    write_id TEXT PRIMARY KEY,
    command_id TEXT,
    attempted_at_utc_ms INTEGER NOT NULL,
    completed_at_utc_ms INTEGER,
    runtime_role INTEGER NOT NULL,
    area INTEGER NOT NULL,
    address INTEGER NOT NULL,
    quantity INTEGER NOT NULL,
    payload_blob BLOB NOT NULL,
    succeeded INTEGER NOT NULL CHECK (succeeded IN (0, 1)),
    error_code TEXT,
    error_message TEXT,
    archive_schema_version INTEGER NOT NULL,
    created_at_utc_ms INTEGER NOT NULL,
    FOREIGN KEY(command_id) REFERENCES equipment_command(command_id) ON DELETE SET NULL
);

CREATE INDEX ix_equipment_command_requested_at ON equipment_command(requested_at_utc_ms);
CREATE INDEX ix_equipment_command_user ON equipment_command(user_id, username);
CREATE INDEX ix_equipment_command_signal ON equipment_command(signal_id);
CREATE INDEX ix_equipment_command_result ON equipment_command(result);
CREATE INDEX ix_modbus_write_attempted_at ON modbus_write(attempted_at_utc_ms);
CREATE INDEX ix_modbus_write_command_id ON modbus_write(command_id);
