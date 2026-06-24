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

CREATE INDEX ix_app_user_enabled ON app_user(is_enabled);
CREATE INDEX ix_app_user_role ON app_user(role);
