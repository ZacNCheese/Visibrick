namespace Visibrick.Sync;

public static class Schema
{
    // Catalog tables are replaced on every import, so never put user data (owned minifigs etc.) in them.
    // Every statement is safe to run repeatedly; it runs at the start of each import.
    public const string Create = """
        CREATE TABLE IF NOT EXISTS minifigs (
            number          text             PRIMARY KEY,   -- BrickLink item number, e.g. sw0001a
            name            text             NOT NULL,
            category_id     integer,
            category_name   text,
            year_released   integer,                        -- null when BrickLink lists it as "?"
            weight_grams    double precision
        );

        -- One row per catalog (e.g. "minifigs"): which file was last imported and a hash of its contents,
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
        ALTER TABLE minifigs     ENABLE ROW LEVEL SECURITY;
        ALTER TABLE import_state ENABLE ROW LEVEL SECURITY;

        DO $$
        DECLARE t text;
        BEGIN
            FOREACH t IN ARRAY ARRAY['minifigs', 'import_state'] LOOP
                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = t AND policyname = 'Anyone can read the catalog') THEN
                    EXECUTE format('CREATE POLICY "Anyone can read the catalog" ON %I FOR SELECT TO anon, authenticated USING (true)', t);
                END IF;
            END LOOP;
        END $$;
        """;
}
