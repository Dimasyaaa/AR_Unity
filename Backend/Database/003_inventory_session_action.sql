-- Add a dedicated history action for completed inventory checks.
-- Existing login/logout/scan/error records and values remain valid.

BEGIN;

ALTER TABLE inventory_sessions
    DROP CONSTRAINT IF EXISTS inventory_sessions_action_check;

ALTER TABLE inventory_sessions
    ADD CONSTRAINT inventory_sessions_action_check
    CHECK (action IN ('login', 'logout', 'scan', 'error', 'inventory'));

INSERT INTO app_schema_migrations (version)
VALUES ('003_inventory_session_action')
ON CONFLICT (version) DO NOTHING;

COMMIT;
