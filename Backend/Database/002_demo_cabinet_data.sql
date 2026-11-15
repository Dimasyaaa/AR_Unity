-- Development-only sample data. Do not run against the production database.
-- QR: DEMO-CABINET-001. The sample deliberately has no photo until a real cabinet image is supplied.

BEGIN;

INSERT INTO qr_codes (code, object_name, is_active)
VALUES ('DEMO-CABINET-001', 'Тестовый шкаф инвентаризации', TRUE)
ON CONFLICT (code) DO NOTHING;

INSERT INTO cabinet_profiles (qr_code_id, title, is_active)
SELECT id, 'Тестовый шкаф', TRUE
FROM qr_codes
WHERE code = 'DEMO-CABINET-001'
ON CONFLICT (qr_code_id) DO NOTHING;

INSERT INTO cabinet_checklist_items (cabinet_id, item_name, sort_order)
SELECT c.id, seed.item_name, seed.sort_order
FROM cabinet_profiles c
JOIN qr_codes q ON q.id = c.qr_code_id
CROSS JOIN (VALUES
    ('Ключ гаечный 13 мм', 10),
    ('Ключ гаечный 17 мм', 20),
    ('Перчатки защитные', 30),
    ('Переносной фонарь', 40)
) AS seed(item_name, sort_order)
WHERE q.code = 'DEMO-CABINET-001'
ON CONFLICT (cabinet_id, item_name) DO NOTHING;

-- Второй шкаф нужен для проверки длинного чек-листа и прокрутки в Unity.
INSERT INTO qr_codes (code, object_name, is_active)
VALUES ('DEMO-CABINET-002', 'Тестовый шкаф с длинным списком', TRUE)
ON CONFLICT (code) DO UPDATE
SET object_name = EXCLUDED.object_name, is_active = TRUE;

INSERT INTO cabinet_profiles (qr_code_id, title, is_active)
SELECT id, 'Тестовый шкаф (18 предметов)', TRUE
FROM qr_codes
WHERE code = 'DEMO-CABINET-002'
ON CONFLICT (qr_code_id) DO UPDATE
SET title = EXCLUDED.title, is_active = TRUE;

-- Состав и порядок совпадают с Assets/StreamingAssets/InventoryDatabase/inventory.db.
CREATE TEMP TABLE expected_demo_cabinet_items ON COMMIT DROP AS
SELECT * FROM (VALUES
    ('Веник промышленный', 10),
    ('Швабра', 20),
    ('Ведро', 30),
    ('Совок металлический', 40),
    ('Щетка ручная', 50),
    ('Перчатки хозяйственные', 60),
    ('Мешки для мусора', 70),
    ('Чистящее средство', 80),
    ('Тряпки хлопчатобумажные', 90),
    ('Скребок для пола', 100),
    ('Запасное ведро', 110),
    ('Рукоятка для швабры', 120),
    ('Щетка для углов', 130),
    ('Знак «Мокрый пол»', 140),
    ('Губки', 150),
    ('Фонарь переносной', 160),
    ('Маска фильтрующая', 170),
    ('Ключ от шкафа', 180)
) AS expected(item_name, sort_order);

UPDATE cabinet_checklist_items item
SET is_active = FALSE
FROM cabinet_profiles cabinet
JOIN qr_codes qr ON qr.id = cabinet.qr_code_id
WHERE qr.code = 'DEMO-CABINET-002'
  AND item.cabinet_id = cabinet.id
  AND NOT EXISTS (
      SELECT 1 FROM expected_demo_cabinet_items expected
      WHERE expected.item_name = item.item_name
  );

INSERT INTO cabinet_checklist_items (cabinet_id, item_name, sort_order, is_active)
SELECT cabinet.id, expected.item_name, expected.sort_order, TRUE
FROM cabinet_profiles cabinet
JOIN qr_codes qr ON qr.id = cabinet.qr_code_id
CROSS JOIN expected_demo_cabinet_items expected
WHERE qr.code = 'DEMO-CABINET-002'
ON CONFLICT (cabinet_id, item_name) DO UPDATE
SET sort_order = EXCLUDED.sort_order, is_active = TRUE;

-- Keep the demo list exactly aligned with the Unity SQLite checklist.
UPDATE cabinet_checklist_items item
SET is_active = FALSE
FROM cabinet_profiles cabinet
JOIN qr_codes qr ON qr.id = cabinet.qr_code_id
WHERE qr.code = 'DEMO-CABINET-002'
  AND item.cabinet_id = cabinet.id
  AND NOT EXISTS (
      SELECT 1 FROM expected_demo_cabinet_items expected
      WHERE expected.item_name = item.item_name
  );

COMMIT;
