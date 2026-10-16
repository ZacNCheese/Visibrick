using System.Diagnostics;
using System.Security.Cryptography;
using Npgsql;
using NpgsqlTypes;
using Visibrick.Core.BrickLink;
using Visibrick.Core.Models;

namespace Visibrick.Sync;

// Imports BrickLink catalog downloads into Postgres (Supabase)
public class CatalogImporter
{
    private const string Columns = "item_type, number, name, category_id, category_name, year_released, alternate_numbers, weight_grams, dimensions";

    private readonly NpgsqlDataSource? _db;
    private readonly bool _force;

    // db is null for a dry run: read and check the files only, nothing is written
    public CatalogImporter(NpgsqlDataSource? db, bool force)
    {
        _db = db;
        _force = force;
    }

    public async Task EnsureSchemaAsync()
    {
        if (_db is null)
            return;

        await using var cmd = _db.CreateCommand(Schema.Create);
        await cmd.ExecuteNonQueryAsync();
    }

    // Recounts the category chips (catalog_categories) after the imports
    public async Task RefreshCategoriesAsync()
    {
        if (_db is null)
            return;

        await using var cmd = _db.CreateCommand(Schema.RefreshCategories);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ImportAsync(string path, CatalogType type)
    {
        var timer = Stopwatch.StartNew();
        var fileName = Path.GetFileName(path);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));

        if (!_force && await GetImportedHashAsync(type) == hash)
        {
            Log($"{fileName}: unchanged since the last import, skipped");
            return;
        }

        // Parse everything before touching the database, so a bad file never wipes the current data
        var file = await BrickLinkCatalogFile.ReadAsync(path, type);
        var items = file.ParseItems();

        if (_db is not null)
            await ReplaceItemsAsync(type, items, fileName, hash);

        Log($"{fileName}: {items.Count:N0} {type.CountNoun} {(_db is null ? "read (dry run)" : "imported")} in {timer.Elapsed.TotalSeconds:F1}s");
    }

    // Replaces every item of one type in one transaction. Readers keep seeing the old rows until it commits,
    // and if anything fails the transaction rolls back and the previous data stays in place.
    private async Task ReplaceItemsAsync(CatalogType type, List<CatalogItem> items, string fileName, string hash)
    {
        await using var conn = await _db!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var delete = new NpgsqlCommand("DELETE FROM catalog_items WHERE item_type = @type", conn, tx))
        {
            delete.Parameters.AddWithValue("type", type.Code);
            await delete.ExecuteNonQueryAsync();
        }

        // COPY ... BINARY is Postgres's bulk-load path, far faster than one INSERT per row
        await using (var importer = await conn.BeginBinaryImportAsync($"COPY catalog_items ({Columns}) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in items)
            {
                await importer.StartRowAsync();
                await WriteAsync(importer, item.Type, NpgsqlDbType.Text);
                await WriteAsync(importer, item.Number, NpgsqlDbType.Text);
                await WriteAsync(importer, item.Name ?? "", NpgsqlDbType.Text);
                await WriteAsync(importer, item.CategoryId, NpgsqlDbType.Integer);
                await WriteAsync(importer, item.CategoryName, NpgsqlDbType.Text);
                await WriteAsync(importer, item.YearReleased, NpgsqlDbType.Integer);
                await WriteAsync(importer, item.AlternateNumbers, NpgsqlDbType.Text);
                await WriteAsync(importer, item.WeightGrams, NpgsqlDbType.Double);
                await WriteAsync(importer, item.Dimensions, NpgsqlDbType.Text);
            }

            await importer.CompleteAsync();
        }

        await using (var save = new NpgsqlCommand("""
            INSERT INTO import_state (kind, file_name, file_hash, row_count, imported_at)
            VALUES (@kind, @file, @hash, @rows, now())
            ON CONFLICT (kind) DO UPDATE
            SET file_name = excluded.file_name, file_hash = excluded.file_hash,
                row_count = excluded.row_count, imported_at = excluded.imported_at
            """, conn, tx))
        {
            save.Parameters.AddWithValue("kind", type.Code);
            save.Parameters.AddWithValue("file", fileName);
            save.Parameters.AddWithValue("hash", hash);
            save.Parameters.AddWithValue("rows", items.Count);
            await save.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
    }

    private static Task WriteAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type)
        => value is null ? importer.WriteNullAsync() : importer.WriteAsync(value, type);

    private async Task<string?> GetImportedHashAsync(CatalogType type)
    {
        if (_db is null)
            return null;

        await using var cmd = _db.CreateCommand("SELECT file_hash FROM import_state WHERE kind = @kind");
        cmd.Parameters.AddWithValue("kind", type.Code);
        return await cmd.ExecuteScalarAsync() as string;
    }

    public static void Log(string message)
        => Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {message}");
}
