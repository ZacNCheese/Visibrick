namespace Visibrick.Sync;

public static class Schema
{
    // Catalog tables are replaced on every sync, so never put user data (owned minifigs etc.) in them
    public const string Create = """
        CREATE TABLE IF NOT EXISTS lego_colors (
            id              integer PRIMARY KEY,
            name            text    NOT NULL,
            rgb             text,
            is_translucent  boolean NOT NULL
        );

        CREATE TABLE IF NOT EXISTS lego_minifigs (
            number      text    PRIMARY KEY,
            name        text    NOT NULL,
            num_parts   integer NOT NULL,
            image_url   text
        );

        -- One row per Rebrickable file: the ETag we last imported, so unchanged files are skipped
        CREATE TABLE IF NOT EXISTS sync_state (
            file_name   text        PRIMARY KEY,
            etag        text,
            row_count   integer     NOT NULL,
            synced_at   timestamptz NOT NULL
        );

        -- Supabase exposes every table in "public" through its REST API, where the (public) anon key
        -- could read and write it. Row Level Security with no policies blocks that API entirely;
        -- this sync connects as the postgres owner, which RLS doesn't apply to.
        ALTER TABLE lego_colors   ENABLE ROW LEVEL SECURITY;
        ALTER TABLE lego_minifigs ENABLE ROW LEVEL SECURITY;
        ALTER TABLE sync_state    ENABLE ROW LEVEL SECURITY;
        """;
}
