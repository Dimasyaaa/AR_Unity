BEGIN;

CREATE TABLE IF NOT EXISTS inventory_run_photos (
    inventory_run_id BIGINT NOT NULL REFERENCES inventory_runs(id) ON DELETE CASCADE,
    photo_index SMALLINT NOT NULL CHECK (photo_index BETWEEN 1 AND 4),
    photo_jpeg BYTEA NOT NULL CHECK (octet_length(photo_jpeg) BETWEEN 4 AND 4194304),
    PRIMARY KEY (inventory_run_id, photo_index)
);

INSERT INTO app_schema_migrations (version)
VALUES ('008_inventory_run_photos')
ON CONFLICT (version) DO NOTHING;

COMMIT;
