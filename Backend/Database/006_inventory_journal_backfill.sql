-- Add historical inventory runs to the general event journal.
-- New runs already write an inventory_sessions row when they are saved.

BEGIN;

WITH run_totals AS (
    SELECT
        r.id,
        r.user_id,
        r.qr_code_id,
        r.completed_at,
        c.title,
        COUNT(i.id) FILTER (WHERE i.status = 'present') AS present_count,
        COUNT(i.id) FILTER (WHERE i.status = 'missing') AS missing_count
    FROM inventory_runs r
    JOIN cabinet_profiles c ON c.id = r.cabinet_id
    LEFT JOIN inventory_run_items i ON i.inventory_run_id = r.id
    GROUP BY r.id, c.title
)
INSERT INTO inventory_sessions (user_id, qr_code_id, action, comment, action_time)
SELECT
    r.user_id,
    r.qr_code_id,
    'inventory',
    format('Инвентаризация «%s»: есть %s, нет %s', r.title, r.present_count, r.missing_count),
    r.completed_at
FROM run_totals r
WHERE NOT EXISTS (
    SELECT 1
    FROM inventory_sessions s
    WHERE s.action = 'inventory'
      AND s.user_id = r.user_id
      AND s.qr_code_id = r.qr_code_id
      AND s.action_time = r.completed_at
);

INSERT INTO app_schema_migrations (version)
VALUES ('006_inventory_journal_backfill')
ON CONFLICT (version) DO NOTHING;

COMMIT;
