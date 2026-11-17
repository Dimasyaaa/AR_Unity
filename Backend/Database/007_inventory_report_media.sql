BEGIN;

CREATE TABLE IF NOT EXISTS inventory_run_media (
    inventory_run_id BIGINT PRIMARY KEY REFERENCES inventory_runs(id) ON DELETE CASCADE,
    photo_jpeg BYTEA NOT NULL CHECK (octet_length(photo_jpeg) BETWEEN 4 AND 4194304),
    voice_wav BYTEA NOT NULL CHECK (octet_length(voice_wav) BETWEEN 46 AND 1920044)
);

INSERT INTO app_schema_migrations (version)
VALUES ('007_inventory_report_media')
ON CONFLICT (version) DO NOTHING;

COMMIT;
