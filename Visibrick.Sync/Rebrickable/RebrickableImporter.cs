using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using Visibrick.Core.Models;

namespace Visibrick.Sync.Rebrickable;

// BrickLink numbers from the BrickLink catalog downloads, used to check Rebrickable's links to BrickLink
// (null when that download isn't in data/bricklink, and then every link is trusted)
public record BrickLinkNumbers(HashSet<string>? Sets, HashSet<string>? Parts, HashSet<string>? Minifigs);

// Imports Rebrickable's downloads (all_downloads.zip from rebrickable.com/downloads) into Postgres (Supabase):
//   - sets and parts into catalog_items (replacing those types), each with its BrickLink number where there is one
//   - what's in each set: its parts (set_parts), minifigs (set_minifigs) and other sets (set_subsets)
//   - what each BrickLink minifig is made of (minifig_parts), using Rebrickable's links to BrickLink numbers
//   - colors, and a picture of each part in each color (part_colors)
public class RebrickableImporter
{
    public const string Kind = "rebrickable";

    private readonly NpgsqlDataSource? _db;
    private readonly bool _force;
    private readonly BrickLinkNumbers _brickLink;

    // Parts and sets are repeated across 1.5 million inventory rows; sharing one string per number saves memory
    private readonly Dictionary<string, string> _strings = new();

    // db is null for a dry run: read and check the zip only, nothing is written
    public RebrickableImporter(NpgsqlDataSource? db, bool force, BrickLinkNumbers brickLink)
    {
        _db = db;
        _force = force;
        _brickLink = brickLink;
    }

    public async Task ImportAsync(string zipPath)
    {
        var timer = Stopwatch.StartNew();
        var fileName = Path.GetFileName(zipPath);

        string hash;
        await using (var stream = File.OpenRead(zipPath))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream));

        if (!_force && await GetImportedHashAsync() == hash)
        {
            CatalogImporter.Log($"{fileName}: unchanged since the last import, skipped");
            return;
        }

        // Read and check everything before touching the database, so a bad file never wipes the current data
        CatalogImporter.Log($"{fileName}: reading...");
        Data data;
        using (var zip = ZipFile.OpenRead(zipPath))
            data = Read(zip);

        foreach (var line in data.Summary())
            CatalogImporter.Log($"  {line}");

        if (_db is not null)
        {
            CatalogImporter.Log($"{fileName}: writing to the database...");
            await WriteAsync(data, fileName, hash);
        }

        CatalogImporter.Log($"{fileName}: {(_db is null ? "read (dry run)" : "imported")} in {timer.Elapsed.TotalSeconds:F1}s");
    }

    // ---------------------------------------------------------------- reading the zip

    private Data Read(ZipArchive zip)
    {
        var data = new Data();

        // Colors, with BrickLink's id for each (preferring the one Rebrickable uses when exporting to BrickLink)
        var blColors = Mappings(Csv.Rows(zip, "color_mappings.csv"), "id", r => r.Bool("is_export_id"));
        foreach (var row in Csv.Rows(zip, "colors.csv"))
        {
            var id = row.Int("id");
            data.Colors.Add(new ColorRow(id, row["name"], row.Text("rgb"), row.Bool("is_trans"),
                blColors.TryGetValue(id.ToString(), out var bl) && int.TryParse(bl, out var blId) ? blId : null));
        }

        // Theme paths for sets, e.g. "Star Wars / Ultimate Collector Series"
        var themes = Csv.Rows(zip, "themes.csv").ToDictionary(r => r.Int("id"), r => (Name: r["name"], Parent: r.IntOrNull("parent_id")));
        string ThemePath(int id)
        {
            var names = new List<string>();
            for (int? at = id; at is { } current && themes.TryGetValue(current, out var theme) && names.Count < 10; at = theme.Parent)
                names.Insert(0, theme.Name);
            return string.Join(" / ", names);
        }

        var partCategories = Csv.Rows(zip, "part_categories.csv").ToDictionary(r => r.Int("id"), r => r["name"]);

        // Rebrickable → BrickLink numbers. set_mappings covers sets and minifigs (fig-...), and only lists sets
        // whose BrickLink number is different; part_mappings lists most parts.
        var setMappings = Csv.Rows(zip, "set_mappings.csv").Where(r => r["ext_system"] == "BL").ToList();
        var blSetNumbers = new Dictionary<string, string>();
        var blFigNumbers = new Dictionary<string, string>();
        foreach (var row in setMappings)
            (row["set_num"].StartsWith("fig-") ? blFigNumbers : blSetNumbers).TryAdd(row["set_num"], row["ext_id"]);
        var blPartNumbers = Mappings(Csv.Rows(zip, "part_mappings.csv"), "part_num", r => r.Bool("is_export_id"));

        // Each set (and minifig) can have several inventory versions; use the newest
        var newest = new Dictionary<string, (int Id, int Version)>();
        foreach (var row in Csv.Rows(zip, "inventories.csv"))
        {
            var owner = row["set_num"];
            var version = row.Int("version");
            if (!newest.TryGetValue(owner, out var current) || version > current.Version)
                newest[owner] = (row.Int("id"), version);
        }
        var inventoryOwner = newest.ToDictionary(n => n.Value.Id, n => Shared(n.Key));

        // Minifigs: Rebrickable's names and pictures, and their BrickLink numbers
        var figs = Csv.Rows(zip, "minifigs.csv").ToDictionary(r => r["fig_num"], r => (Name: r["name"], NumParts: r.Int("num_parts"), Image: r.Text("img_url")));
        string? BrickLinkFig(string fig) =>
            blFigNumbers.TryGetValue(fig, out var bl) && (_brickLink.Minifigs?.Contains(bl) ?? true) ? bl : null;

        // What's in each set
        var setFigs = new Dictionary<(string Set, string Fig), int>();
        foreach (var row in Csv.Rows(zip, "inventory_minifigs.csv"))
            if (inventoryOwner.TryGetValue(row.Int("inventory_id"), out var set) && !set.StartsWith("fig-"))
                Add(setFigs, (set, row["fig_num"]), row.Int("quantity"));

        var setSubsets = new Dictionary<(string Set, string Child), int>();
        foreach (var row in Csv.Rows(zip, "inventory_sets.csv"))
            if (inventoryOwner.TryGetValue(row.Int("inventory_id"), out var set))
                Add(setSubsets, (set, Shared(row["set_num"])), row.Int("quantity"));

        // The big one: 1.5 million rows of part + color + quantity, for both sets and minifigs
        var setParts = new Dictionary<PartKey, int>();
        var figParts = new Dictionary<PartKey, int>();
        var partColors = new Dictionary<(string Part, int Color), (string? Image, int Uses)>();
        foreach (var row in Csv.Rows(zip, "inventory_parts.csv"))
        {
            if (!inventoryOwner.TryGetValue(row.Int("inventory_id"), out var owner))
                continue;

            var key = new PartKey(owner, Shared(row["part_num"]), row.Int("color_id"), row.Bool("is_spare"));
            var isFig = owner.StartsWith("fig-");
            Add(isFig ? figParts : setParts, key, row.Int("quantity"));

            // One picture per part + color, and how often that combination is used (to pick each part's main picture)
            var colorKey = (key.Part, key.Color);
            partColors.TryGetValue(colorKey, out var seen);
            partColors[colorKey] = (seen.Image ?? row.Text("img_url"), seen.Uses + (isFig || key.Spare ? 0 : 1));
        }

        // Sets. Rebrickable also lists merchandise (bags, keychains, clothing, books...) as sets with no pieces;
        // those are left out, since BrickLink's Gear and Books cover them.
        var setsWithFigs = setFigs.Keys.Select(k => k.Set).ToHashSet();
        var setsWithSubsets = setSubsets.Keys.Select(k => k.Set).ToHashSet();
        foreach (var row in Csv.Rows(zip, "sets.csv"))
        {
            var number = Shared(row["set_num"]);
            var numParts = row.Int("num_parts");
            if (numParts == 0 && !setsWithFigs.Contains(number) && !setsWithSubsets.Contains(number))
            {
                data.SkippedMerchandise++;
                continue;
            }

            // set_mappings only lists sets whose BrickLink number is different; others have the same number
            var bl = blSetNumbers.GetValueOrDefault(number) ?? number;
            if (_brickLink.Sets is { } known && !known.Contains(bl))
                bl = null;

            data.Sets.Add(new CatalogItem
            {
                Type = CatalogType.Sets.Code,
                Number = number,
                Name = row["name"],
                CategoryId = row.Int("theme_id"),
                CategoryName = ThemePath(row.Int("theme_id")),
                YearReleased = row.IntOrNull("year"),
                BrickLinkNumber = bl,
                SourceImageUrl = row.Text("img_url"),
                NumParts = numParts
            });
        }
        var includedSets = data.Sets.Select(s => s.Number).ToHashSet();

        // Parts, each with the picture of the color it's most used in
        var mainPicture = partColors
            .Where(p => p.Value.Image is not null)
            .GroupBy(p => p.Key.Part)
            .ToDictionary(g => g.Key, g => g.MaxBy(p => p.Value.Uses).Value.Image);
        foreach (var row in Csv.Rows(zip, "parts.csv"))
        {
            var number = Shared(row["part_num"]);

            // Most parts are mapped; an unmapped part may still have the same number on BrickLink
            var bl = blPartNumbers.GetValueOrDefault(number);
            if (_brickLink.Parts is { } known)
                bl = bl is not null && known.Contains(bl) ? bl : known.Contains(number) ? number : null;

            data.Parts.Add(new CatalogItem
            {
                Type = CatalogType.Parts.Code,
                Number = number,
                Name = row["name"],
                CategoryId = row.Int("part_cat_id"),
                CategoryName = partCategories.GetValueOrDefault(row.Int("part_cat_id")),
                BrickLinkNumber = bl,
                SourceImageUrl = mainPicture.GetValueOrDefault(number)
            });
        }

        // Relationships, for the sets that were kept
        data.SetParts = setParts.Where(p => includedSets.Contains(p.Key.Owner)).ToList();
        data.SetSubsets = setSubsets.Where(s => includedSets.Contains(s.Key.Set) && includedSets.Contains(s.Key.Child)).ToList();
        foreach (var ((set, fig), quantity) in setFigs)
        {
            if (!includedSets.Contains(set))
                continue;

            var info = figs.GetValueOrDefault(fig);
            data.SetMinifigs.Add(new SetMinifigRow(set, fig, BrickLinkFig(fig), info.Name ?? fig, info.Image, info.NumParts, quantity));
        }

        // Minifig parts, keyed by BrickLink number. A few BrickLink minifigs match two Rebrickable ones; use the first.
        var figForBrickLink = new Dictionary<string, string>();
        foreach (var fig in figs.Keys.Order())
            if (BrickLinkFig(fig) is { } bl)
                figForBrickLink.TryAdd(bl, fig);
        var brickLinkForFig = figForBrickLink.ToDictionary(f => f.Value, f => f.Key);
        data.MinifigParts = figParts
            .Where(p => brickLinkForFig.ContainsKey(p.Key.Owner))
            .Select(p => new KeyValuePair<PartKey, int>(p.Key with { Owner = brickLinkForFig[p.Key.Owner] }, p.Value))
            .ToList();

        data.PartColors = partColors.Select(p => new PartColorRow(p.Key.Part, p.Key.Color, p.Value.Image, p.Value.Uses)).ToList();
        data.MinifigsWithBrickLink = figs.Keys.Count(f => BrickLinkFig(f) is not null);
        data.MinifigsTotal = figs.Count;
        return data;
    }

    // Rebrickable id → BrickLink id from a *_mappings.csv file, preferring rows the preferred() test picks
    private static Dictionary<string, string> Mappings(IEnumerable<CsvRow> rows, string idColumn, Func<CsvRow, bool> preferred)
    {
        var result = new Dictionary<string, string>();
        foreach (var row in rows)
        {
            if (row["ext_system"] != "BL")
                continue;
            if (preferred(row))
                result[row[idColumn]] = row["ext_id"];
            else
                result.TryAdd(row[idColumn], row["ext_id"]);
        }
        return result;
    }

    private static void Add<TKey>(Dictionary<TKey, int> totals, TKey key, int quantity) where TKey : notnull
        => totals[key] = totals.GetValueOrDefault(key) + quantity;

    private string Shared(string value)
    {
        if (_strings.TryGetValue(value, out var shared))
            return shared;
        _strings[value] = value;
        return value;
    }

    // ---------------------------------------------------------------- writing to the database

    // Replaces sets, parts and every relationship table in one transaction. Readers keep seeing the old data
    // until it commits, and if anything fails it all rolls back and the previous data stays in place.
    private async Task WriteAsync(Data data, string fileName, string hash)
    {
        await using var conn = await _db!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await ExecuteAsync(conn, tx, "DELETE FROM catalog_items WHERE item_type IN ('S', 'P')");
        await ExecuteAsync(conn, tx, "TRUNCATE colors, part_colors, set_parts, set_minifigs, set_subsets, minifig_parts");

        await CopyAsync(conn, "catalog_items (item_type, number, name, category_id, category_name, year_released, bricklink_number, image_url, num_parts)",
            data.Sets.Concat(data.Parts), async (w, item) =>
            {
                await Write(w, item.Type, NpgsqlDbType.Text);
                await Write(w, item.Number, NpgsqlDbType.Text);
                await Write(w, item.Name ?? "", NpgsqlDbType.Text);
                await Write(w, item.CategoryId, NpgsqlDbType.Integer);
                await Write(w, item.CategoryName, NpgsqlDbType.Text);
                await Write(w, item.YearReleased, NpgsqlDbType.Integer);
                await Write(w, item.BrickLinkNumber, NpgsqlDbType.Text);
                await Write(w, item.SourceImageUrl, NpgsqlDbType.Text);
                await Write(w, item.NumParts, NpgsqlDbType.Integer);
            });

        await CopyAsync(conn, "colors (id, name, rgb, is_trans, bricklink_id)", data.Colors, async (w, c) =>
        {
            await Write(w, c.Id, NpgsqlDbType.Integer);
            await Write(w, c.Name, NpgsqlDbType.Text);
            await Write(w, c.Rgb, NpgsqlDbType.Text);
            await Write(w, c.IsTrans, NpgsqlDbType.Boolean);
            await Write(w, c.BrickLinkId, NpgsqlDbType.Integer);
        });

        await CopyAsync(conn, "part_colors (part_num, color_id, image_url, set_count)", data.PartColors, async (w, p) =>
        {
            await Write(w, p.Part, NpgsqlDbType.Text);
            await Write(w, p.Color, NpgsqlDbType.Integer);
            await Write(w, p.Image, NpgsqlDbType.Text);
            await Write(w, p.Uses, NpgsqlDbType.Integer);
        });

        await CopyAsync(conn, "set_parts (set_num, part_num, color_id, is_spare, quantity)", data.SetParts, WritePart);
        await CopyAsync(conn, "minifig_parts (minifig_number, part_num, color_id, is_spare, quantity)", data.MinifigParts, WritePart);

        await CopyAsync(conn, "set_minifigs (set_num, fig_num, bricklink_number, name, image_url, num_parts, quantity)", data.SetMinifigs, async (w, m) =>
        {
            await Write(w, m.Set, NpgsqlDbType.Text);
            await Write(w, m.Fig, NpgsqlDbType.Text);
            await Write(w, m.BrickLinkNumber, NpgsqlDbType.Text);
            await Write(w, m.Name, NpgsqlDbType.Text);
            await Write(w, m.Image, NpgsqlDbType.Text);
            await Write(w, m.NumParts, NpgsqlDbType.Integer);
            await Write(w, m.Quantity, NpgsqlDbType.Integer);
        });

        await CopyAsync(conn, "set_subsets (set_num, child_set_num, quantity)", data.SetSubsets, async (w, s) =>
        {
            await Write(w, s.Key.Set, NpgsqlDbType.Text);
            await Write(w, s.Key.Child, NpgsqlDbType.Text);
            await Write(w, s.Value, NpgsqlDbType.Integer);
        });

        await SaveStateAsync(conn, tx, CatalogType.Sets.Code, fileName, hash, data.Sets.Count);
        await SaveStateAsync(conn, tx, CatalogType.Parts.Code, fileName, hash, data.Parts.Count);
        await SaveStateAsync(conn, tx, Kind, fileName, hash, data.SetParts.Count);

        await tx.CommitAsync();

        // Fresh statistics so Postgres plans queries on the new data well
        await ExecuteAsync(conn, null, "ANALYZE catalog_items, colors, part_colors, set_parts, set_minifigs, set_subsets, minifig_parts");
    }

    private static async Task WritePart(NpgsqlBinaryImporter w, KeyValuePair<PartKey, int> p)
    {
        await Write(w, p.Key.Owner, NpgsqlDbType.Text);
        await Write(w, p.Key.Part, NpgsqlDbType.Text);
        await Write(w, p.Key.Color, NpgsqlDbType.Integer);
        await Write(w, p.Key.Spare, NpgsqlDbType.Boolean);
        await Write(w, p.Value, NpgsqlDbType.Integer);
    }

    // COPY ... BINARY is Postgres's bulk-load path, far faster than one INSERT per row
    private static async Task CopyAsync<T>(NpgsqlConnection conn, string table, IEnumerable<T> rows, Func<NpgsqlBinaryImporter, T, Task> write)
    {
        await using var importer = await conn.BeginBinaryImportAsync($"COPY {table} FROM STDIN (FORMAT BINARY)");
        importer.Timeout = TimeSpan.FromMinutes(10);
        foreach (var row in rows)
        {
            await importer.StartRowAsync();
            await write(importer, row);
        }
        await importer.CompleteAsync();
    }

    private static Task Write<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type)
        => value is null ? importer.WriteNullAsync() : importer.WriteAsync(value, type);

    private static async Task ExecuteAsync(NpgsqlConnection conn, NpgsqlTransaction? tx, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = 600 };
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SaveStateAsync(NpgsqlConnection conn, NpgsqlTransaction tx, string kind, string fileName, string hash, int rows)
    {
        await using var save = new NpgsqlCommand("""
            INSERT INTO import_state (kind, file_name, file_hash, row_count, imported_at)
            VALUES (@kind, @file, @hash, @rows, now())
            ON CONFLICT (kind) DO UPDATE
            SET file_name = excluded.file_name, file_hash = excluded.file_hash,
                row_count = excluded.row_count, imported_at = excluded.imported_at
            """, conn, tx);
        save.Parameters.AddWithValue("kind", kind);
        save.Parameters.AddWithValue("file", fileName);
        save.Parameters.AddWithValue("hash", hash);
        save.Parameters.AddWithValue("rows", rows);
        await save.ExecuteNonQueryAsync();
    }

    private async Task<string?> GetImportedHashAsync()
    {
        if (_db is null)
            return null;

        await using var cmd = _db.CreateCommand("SELECT file_hash FROM import_state WHERE kind = @kind");
        cmd.Parameters.AddWithValue("kind", Kind);
        return await cmd.ExecuteScalarAsync() as string;
    }

    // ---------------------------------------------------------------- what was read

    // Owner is a set number, or a minifig (Rebrickable fig-... while reading, BrickLink number once mapped)
    private readonly record struct PartKey(string Owner, string Part, int Color, bool Spare);

    private record ColorRow(int Id, string Name, string? Rgb, bool IsTrans, int? BrickLinkId);
    private record PartColorRow(string Part, int Color, string? Image, int Uses);
    private record SetMinifigRow(string Set, string Fig, string? BrickLinkNumber, string Name, string? Image, int NumParts, int Quantity);

    private class Data
    {
        public List<CatalogItem> Sets { get; } = [];
        public List<CatalogItem> Parts { get; } = [];
        public List<ColorRow> Colors { get; } = [];
        public List<PartColorRow> PartColors { get; set; } = [];
        public List<KeyValuePair<PartKey, int>> SetParts { get; set; } = [];
        public List<KeyValuePair<PartKey, int>> MinifigParts { get; set; } = [];
        public List<SetMinifigRow> SetMinifigs { get; } = [];
        public List<KeyValuePair<(string Set, string Child), int>> SetSubsets { get; set; } = [];
        public int SkippedMerchandise { get; set; }
        public int MinifigsWithBrickLink { get; set; }
        public int MinifigsTotal { get; set; }

        public IEnumerable<string> Summary()
        {
            yield return $"{Sets.Count:N0} sets ({SkippedMerchandise:N0} merchandise items with no pieces left out), " +
                         $"{Sets.Count(s => s.BrickLinkNumber is not null):N0} with a BrickLink number";
            yield return $"{Parts.Count:N0} parts, {Parts.Count(p => p.BrickLinkNumber is not null):N0} with a BrickLink number";
            yield return $"{Colors.Count:N0} colors, {PartColors.Count:N0} part + color pictures";
            yield return $"{SetParts.Count:N0} set → part rows, {SetSubsets.Count:N0} set → set rows";
            yield return $"{SetMinifigs.Count:N0} set → minifig rows, {SetMinifigs.Count(m => m.BrickLinkNumber is not null):N0} linked to a BrickLink minifig";
            yield return $"{MinifigsWithBrickLink:N0} of {MinifigsTotal:N0} Rebrickable minifigs matched to BrickLink; {MinifigParts.Count:N0} minifig → part rows";
        }
    }
}
