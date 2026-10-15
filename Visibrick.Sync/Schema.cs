namespace Visibrick.Sync;

public static class Schema
{
    // Catalog tables are replaced on every import, so never put user data (owned items etc.) in them.
    // Every statement is safe to run repeatedly; it runs at the start of each import, and never removes anything.
    public const string Create = """
        -- Every BrickLink catalog item (minifigs, sets, parts, gear, books) in one table
        CREATE TABLE IF NOT EXISTS catalog_items (
            item_type          text             NOT NULL,    -- BrickLink item type letter: M, S, P, G, B
            number             text             NOT NULL,    -- BrickLink item number, e.g. sw0001a, 75192-1, 3001
            name               text             NOT NULL,
            category_id        integer,
            category_name      text,
            year_released      integer,                      -- null when unknown, and for parts
            alternate_numbers  text,                         -- parts only
            weight_grams       double precision,
            dimensions         text,                         -- e.g. "2 x 4 x 4"
            PRIMARY KEY (item_type, number)
        );

        -- One row per item type (kind = M, S, P, G, B): which file was last imported and a hash of its contents,
        -- so unchanged files are skipped and the app can tell when to download a fresh copy
        CREATE TABLE IF NOT EXISTS import_state (
            kind         text        PRIMARY KEY,
            file_name    text        NOT NULL,
            file_hash    text        NOT NULL,
            row_count    integer     NOT NULL,
            imported_at  timestamptz NOT NULL
        );

        -- Supabase exposes every table in "public" through its REST API. Row Level Security limits that API to
        -- what the policies below allow: anyone (the app, using the public anon key) may read the catalog,
        -- nobody may change it. This import connects as the postgres owner, which RLS doesn't apply to.
        ALTER TABLE catalog_items ENABLE ROW LEVEL SECURITY;
        ALTER TABLE import_state  ENABLE ROW LEVEL SECURITY;

        DO $$
        DECLARE t text;
        BEGIN
            FOREACH t IN ARRAY ARRAY['catalog_items', 'import_state'] LOOP
                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = t AND policyname = 'Anyone can read the catalog') THEN
                    EXECUTE format('CREATE POLICY "Anyone can read the catalog" ON %I FOR SELECT TO anon, authenticated USING (true)', t);
                END IF;
            END LOOP;
        END $$;
        """;
}
