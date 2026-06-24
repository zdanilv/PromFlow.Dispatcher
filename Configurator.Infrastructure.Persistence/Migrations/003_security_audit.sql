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

CREATE INDEX ix_security_audit_occurred_at ON security_audit(occurred_at_utc_ms);
CREATE INDEX ix_security_audit_actor ON security_audit(actor_user_id, actor_username);
CREATE INDEX ix_security_audit_target ON security_audit(target_user_id);
CREATE INDEX ix_security_audit_event_result ON security_audit(event_type, result);
