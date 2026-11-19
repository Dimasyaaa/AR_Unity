-- Объединяет фото и голосовую запись отчёта в единую таблицу вложений.
-- Перед запуском сохраните резервную копию PostgreSQL.
-- Перенос, проверка и удаление старых таблиц выполняются одной транзакцией.

BEGIN;

CREATE TABLE IF NOT EXISTS inventory_run_attachments (
    inventory_run_id BIGINT NOT NULL REFERENCES inventory_runs(id) ON DELETE CASCADE,
    attachment_type TEXT NOT NULL CHECK (attachment_type IN ('photo', 'voice')),
    attachment_index SMALLINT NOT NULL,
    content_type TEXT NOT NULL CHECK (
        (attachment_type = 'photo' AND content_type IN ('image/jpeg', 'image/png', 'image/webp')) OR
        (attachment_type = 'voice' AND content_type = 'audio/wav')
    ),
    data BYTEA NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (inventory_run_id, attachment_type, attachment_index),
    CONSTRAINT ck_inventory_run_attachment_index CHECK (
        (attachment_type = 'photo' AND attachment_index BETWEEN 0 AND 4) OR
        (attachment_type = 'voice' AND attachment_index = 0)
    ),
    CONSTRAINT ck_inventory_run_attachment_size CHECK (
        (attachment_type = 'photo' AND octet_length(data) BETWEEN 4 AND 4194304) OR
        (attachment_type = 'voice' AND octet_length(data) BETWEEN 46 AND 1920044)
    )
);

DO $$
BEGIN
    -- Первая фотография и голос ранее хранились в одной строке.
    IF to_regclass('public.inventory_run_media') IS NOT NULL THEN
        INSERT INTO inventory_run_attachments
            (inventory_run_id, attachment_type, attachment_index, content_type, data)
        SELECT inventory_run_id, 'photo', 0, 'image/jpeg', photo_jpeg
        FROM inventory_run_media
        ON CONFLICT (inventory_run_id, attachment_type, attachment_index) DO NOTHING;

        INSERT INTO inventory_run_attachments
            (inventory_run_id, attachment_type, attachment_index, content_type, data)
        SELECT inventory_run_id, 'voice', 0, 'audio/wav', voice_wav
        FROM inventory_run_media
        ON CONFLICT (inventory_run_id, attachment_type, attachment_index) DO NOTHING;

        IF EXISTS (
            SELECT 1
            FROM inventory_run_media old
            LEFT JOIN inventory_run_attachments photo
                ON photo.inventory_run_id = old.inventory_run_id
               AND photo.attachment_type = 'photo'
               AND photo.attachment_index = 0
            LEFT JOIN inventory_run_attachments voice
                ON voice.inventory_run_id = old.inventory_run_id
               AND voice.attachment_type = 'voice'
               AND voice.attachment_index = 0
            WHERE photo.data IS DISTINCT FROM old.photo_jpeg
               OR voice.data IS DISTINCT FROM old.voice_wav
        ) THEN
            RAISE EXCEPTION 'Проверка переноса вложений не пройдена; старые таблицы сохранены.';
        END IF;
    END IF;

    -- Остальные фотографии ранее хранились с индексами 1–4.
    IF to_regclass('public.inventory_run_photos') IS NOT NULL THEN
        INSERT INTO inventory_run_attachments
            (inventory_run_id, attachment_type, attachment_index, content_type, data)
        SELECT inventory_run_id, 'photo', photo_index, 'image/jpeg', photo_jpeg
        FROM inventory_run_photos
        ON CONFLICT (inventory_run_id, attachment_type, attachment_index) DO NOTHING;

        IF EXISTS (
            SELECT 1
            FROM inventory_run_photos old
            LEFT JOIN inventory_run_attachments photo
                ON photo.inventory_run_id = old.inventory_run_id
               AND photo.attachment_type = 'photo'
               AND photo.attachment_index = old.photo_index
            WHERE photo.data IS DISTINCT FROM old.photo_jpeg
        ) THEN
            RAISE EXCEPTION 'Проверка переноса фотографий не пройдена; старые таблицы сохранены.';
        END IF;
    END IF;
END $$;

DROP TABLE IF EXISTS inventory_run_photos;
DROP TABLE IF EXISTS inventory_run_media;

CREATE TABLE IF NOT EXISTS app_schema_migrations (
    version TEXT PRIMARY KEY,
    applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

INSERT INTO app_schema_migrations (version)
VALUES ('009_inventory_run_attachments')
ON CONFLICT (version) DO NOTHING;

COMMIT;
