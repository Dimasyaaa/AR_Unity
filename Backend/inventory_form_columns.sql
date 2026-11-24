-- Однократное добавление полей бланка подсчёта в существующие таблицы.
-- Выполнить в pgAdmin на базе проекта до запуска обновлённого Backend.
-- Скрипт не создаёт таблиц и не удаляет имеющиеся данные; повторный запуск безопасен.

ALTER TABLE box_items
    ADD COLUMN IF NOT EXISTS structural_division TEXT,
    ADD COLUMN IF NOT EXISTS warehouse TEXT,
    ADD COLUMN IF NOT EXISTS ens_code TEXT,
    ADD COLUMN IF NOT EXISTS account_code TEXT,
    ADD COLUMN IF NOT EXISTS mol TEXT,
    ADD COLUMN IF NOT EXISTS storage_location TEXT,
    ADD COLUMN IF NOT EXISTS unit TEXT;

-- Сохраняем исходные реквизиты в результате проверки,
-- чтобы старый отчёт не менялся после редактирования карточки коробки.
ALTER TABLE inventory_run_items
    ADD COLUMN IF NOT EXISTS structural_division_snapshot TEXT,
    ADD COLUMN IF NOT EXISTS warehouse_snapshot TEXT,
    ADD COLUMN IF NOT EXISTS ens_code_snapshot TEXT,
    ADD COLUMN IF NOT EXISTS account_code_snapshot TEXT,
    ADD COLUMN IF NOT EXISTS mol_snapshot TEXT,
    ADD COLUMN IF NOT EXISTS storage_location_snapshot TEXT,
    ADD COLUMN IF NOT EXISTS unit_snapshot TEXT;
