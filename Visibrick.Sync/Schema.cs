namespace Visibrick.Sync;

public static class Schema
{
    // Catalog tables are replaced on every import, so never put user data (owned items etc.) in them.
    // Every statement is safe to run repeatedly; it runs at the start of each import, and never removes anything.
    public const string Create = """
        -- Every catalog item in one table: minifigs, gear and books from BrickLink; sets and parts from Rebrickable
        CREATE TABLE IF NOT EXISTS catalog_items (
            item_type          text             NOT NULL,    -- item type letter: M, S, P, G, B
            number             text             NOT NULL,    -- number from its source, e.g. sw0001a, 75192-1, 3001
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

        -- Sets and parts come from Rebrickable, which adds these
        ALTER TABLE catalog_items ADD COLUMN IF NOT EXISTS bricklink_number text;   -- the same item's BrickLink number, if any
        ALTER TABLE catalog_items ADD COLUMN IF NOT EXISTS image_url        text;   -- Rebrickable's picture
        ALTER TABLE catalog_items ADD COLUMN IF NOT EXISTS num_parts        integer; -- sets: how many pieces

        -- ---------------------------------------------------------------- what's in each set (from Rebrickable)

        CREATE TABLE IF NOT EXISTS colors (
            id            integer PRIMARY KEY,     -- Rebrickable color id
            name          text    NOT NULL,
            rgb           text,                    -- e.g. "05131D"
            is_trans      boolean NOT NULL,
            bricklink_id  integer
        );

        -- A picture of each part in each color, and how many sets use that combination
        CREATE TABLE IF NOT EXISTS part_colors (
            part_num   text    NOT NULL,
            color_id   integer NOT NULL,
            image_url  text,
            set_count  integer NOT NULL,
            PRIMARY KEY (part_num, color_id)
        );

        -- The parts in each set (its newest inventory). Spare parts are rows of their own.
        CREATE TABLE IF NOT EXISTS set_parts (
            set_num   text    NOT NULL,
            part_num  text    NOT NULL,
            color_id  integer NOT NULL,
            is_spare  boolean NOT NULL,
            quantity  integer NOT NULL,
            PRIMARY KEY (set_num, part_num, color_id, is_spare)
        );
        CREATE INDEX IF NOT EXISTS set_parts_part ON set_parts (part_num);

        -- The minifigs in each set; bricklink_number links to the minifig in catalog_items (null when BrickLink doesn't have it)
        CREATE TABLE IF NOT EXISTS set_minifigs (
            set_num           text    NOT NULL,
            fig_num           text    NOT NULL,   -- Rebrickable minifig number, e.g. fig-000001
            bricklink_number  text,
            name              text    NOT NULL,
            image_url         text,
            num_parts         integer NOT NULL,
            quantity          integer NOT NULL,
            PRIMARY KEY (set_num, fig_num)
        );
        CREATE INDEX IF NOT EXISTS set_minifigs_bricklink ON set_minifigs (bricklink_number);

        -- Sets that come inside other sets (bundles, value packs)
        CREATE TABLE IF NOT EXISTS set_subsets (
            set_num        text    NOT NULL,
            child_set_num  text    NOT NULL,
            quantity       integer NOT NULL,
            PRIMARY KEY (set_num, child_set_num)
        );
        CREATE INDEX IF NOT EXISTS set_subsets_child ON set_subsets (child_set_num);

        -- The parts each BrickLink minifig is made of
        CREATE TABLE IF NOT EXISTS minifig_parts (
            minifig_number  text    NOT NULL,   -- BrickLink number, e.g. sw0001a
            part_num        text    NOT NULL,
            color_id        integer NOT NULL,
            is_spare        boolean NOT NULL,
            quantity        integer NOT NULL,
            PRIMARY KEY (minifig_number, part_num, color_id, is_spare)
        );
        CREATE INDEX IF NOT EXISTS minifig_parts_part ON minifig_parts (part_num);

        -- Supabase exposes every table in "public" through its REST API. Row Level Security limits that API to
        -- what the policies below allow: anyone (the app, using the public anon key) may read the catalog,
        -- nobody may change it. This import connects as the postgres owner, which RLS doesn't apply to.
        ALTER TABLE catalog_items ENABLE ROW LEVEL SECURITY;
        ALTER TABLE import_state  ENABLE ROW LEVEL SECURITY;
        ALTER TABLE colors        ENABLE ROW LEVEL SECURITY;
        ALTER TABLE part_colors   ENABLE ROW LEVEL SECURITY;
        ALTER TABLE set_parts     ENABLE ROW LEVEL SECURITY;
        ALTER TABLE set_minifigs  ENABLE ROW LEVEL SECURITY;
        ALTER TABLE set_subsets   ENABLE ROW LEVEL SECURITY;
        ALTER TABLE minifig_parts ENABLE ROW LEVEL SECURITY;

        DO $$
        DECLARE t text;
        BEGIN
            FOREACH t IN ARRAY ARRAY['catalog_items', 'import_state', 'colors', 'part_colors', 'set_parts', 'set_minifigs', 'set_subsets', 'minifig_parts'] LOOP
                IF NOT EXISTS (SELECT 1 FROM pg_policies WHERE tablename = t AND policyname = 'Anyone can read the catalog') THEN
                    EXECUTE format('CREATE POLICY "Anyone can read the catalog" ON %I FOR SELECT TO anon, authenticated USING (true)', t);
                END IF;
            END LOOP;
        END $$;

        -- ---------------------------------------------------------------- views the website reads
        -- security_invoker makes each view run as the person asking, so the read-only policies above still apply.

        -- A set's parts, with names, colors and pictures: set_part_list?set_num=eq.75192-1
        CREATE OR REPLACE VIEW set_part_list WITH (security_invoker = on) AS
        SELECT sp.set_num, sp.part_num, p.name AS part_name, p.bricklink_number,
               sp.color_id, c.name AS color_name, c.rgb, c.is_trans,
               sp.quantity, sp.is_spare, coalesce(pc.image_url, p.image_url) AS image_url
        FROM set_parts sp
        LEFT JOIN catalog_items p ON p.item_type = 'P' AND p.number = sp.part_num
        LEFT JOIN colors c ON c.id = sp.color_id
        LEFT JOIN part_colors pc ON pc.part_num = sp.part_num AND pc.color_id = sp.color_id;

        -- A minifig's parts: minifig_part_list?minifig_number=eq.sw0001a
        CREATE OR REPLACE VIEW minifig_part_list WITH (security_invoker = on) AS
        SELECT mp.minifig_number, mp.part_num, p.name AS part_name, p.bricklink_number,
               mp.color_id, c.name AS color_name, c.rgb, c.is_trans,
               mp.quantity, mp.is_spare, coalesce(pc.image_url, p.image_url) AS image_url
        FROM minifig_parts mp
        LEFT JOIN catalog_items p ON p.item_type = 'P' AND p.number = mp.part_num
        LEFT JOIN colors c ON c.id = mp.color_id
        LEFT JOIN part_colors pc ON pc.part_num = mp.part_num AND pc.color_id = mp.color_id;

        -- The sets a part appears in (all colors together, spares not counted): part_set_list?part_num=eq.3001
        CREATE OR REPLACE VIEW part_set_list WITH (security_invoker = on) AS
        SELECT sp.part_num, sp.set_num, s.name, s.year_released, s.image_url, s.num_parts, sum(sp.quantity)::integer AS quantity
        FROM set_parts sp
        JOIN catalog_items s ON s.item_type = 'S' AND s.number = sp.set_num
        WHERE NOT sp.is_spare
        GROUP BY sp.part_num, sp.set_num, s.name, s.year_released, s.image_url, s.num_parts;

        -- The sets a BrickLink minifig appears in: minifig_set_list?minifig_number=eq.sw0001a
        CREATE OR REPLACE VIEW minifig_set_list WITH (security_invoker = on) AS
        SELECT sm.bricklink_number AS minifig_number, sm.set_num, s.name, s.year_released, s.image_url, s.num_parts, sm.quantity
        FROM set_minifigs sm
        JOIN catalog_items s ON s.item_type = 'S' AND s.number = sm.set_num
        WHERE sm.bricklink_number IS NOT NULL;

        -- The sets inside a set: set_subset_list?set_num=eq.66xxx-1
        CREATE OR REPLACE VIEW set_subset_list WITH (security_invoker = on) AS
        SELECT ss.set_num, ss.child_set_num, s.name, s.year_released, s.image_url, s.num_parts, ss.quantity
        FROM set_subsets ss
        JOIN catalog_items s ON s.item_type = 'S' AND s.number = ss.child_set_num;

        -- The colors a part comes in: part_color_list?part_num=eq.3001
        CREATE OR REPLACE VIEW part_color_list WITH (security_invoker = on) AS
        SELECT pc.part_num, pc.color_id, c.name AS color_name, c.rgb, c.is_trans, pc.image_url, pc.set_count
        FROM part_colors pc
        JOIN colors c ON c.id = pc.color_id;

        GRANT SELECT ON set_part_list, minifig_part_list, part_set_list, minifig_set_list, set_subset_list, part_color_list TO anon, authenticated;

        -- Tell Supabase's REST API about any new tables and views
        NOTIFY pgrst, 'reload schema';
        """;
}
