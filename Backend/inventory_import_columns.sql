-- Дополнительные поля для общей инвентаризации из загруженной описи.
-- Выполнить один раз в pgAdmin перед запуском обновлённого Backend.
-- Старые назначения и данные остаются в таблице; новые описи не привязаны к сотруднику.

ALTER TABLE inventory_batches
    ALTER COLUMN user_id DROP NOT NULL,
    ADD COLUMN IF NOT EXISTS inventory_title TEXT,
    ADD COLUMN IF NOT EXISTS source_file_name TEXT,
    ADD COLUMN IF NOT EXISTS source_file_data BYTEA;

ALTER TABLE inventory_batch_boxes
    ADD COLUMN IF NOT EXISTS sort_order INTEGER;

-- В общем режиме строки проверки и журнала не принадлежат конкретному сотруднику.
ALTER TABLE inventory_runs
    ALTER COLUMN user_id DROP NOT NULL;

ALTER TABLE inventory_sessions
    ALTER COLUMN user_id DROP NOT NULL;
