-- Allow calculator calculations to be synchronized as a distinct journal action.
BEGIN;

ALTER TABLE inventory_sessions
    DROP CONSTRAINT IF EXISTS inventory_sessions_action_check;

ALTER TABLE inventory_sessions
    ADD CONSTRAINT inventory_sessions_action_check
    CHECK (action IN ('login', 'logout', 'scan', 'error', 'inventory', 'calc'));

INSERT INTO app_schema_migrations (version)
VALUES ('005_calculator_action')
ON CONFLICT (version) DO NOTHING;

COMMIT;
