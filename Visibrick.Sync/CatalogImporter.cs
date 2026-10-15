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

    public async Task ImportAsync(string path, CatalogKind kind)
    {
        var timer = Stopwatch.StartNew();
        var fileName = Path.GetFileName(path);
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var kindName = KindName(kind);

        if (!_force && await GetImportedHashAsync(kindName) == hash)
        {
            Log($"{fileName}: unchanged since the last import, skipped");
            return;
        }

        var file = await BrickLinkCatalogFile.ReadAsync(path, kind);

        // Parse everything before touching the database, so a bad file never wipes the current data
        var minifigs = file.ParseMinifigs();
        var rows = _db is null
            ? minifigs.Count
            : await ReplaceTableAsync("minifigs", "number, name, category_id, category_name, year_released, weight_grams", minifigs, WriteMinifigAsync, kindName, fileName, hash);

        Log($"{fileName}: {rows:N0} {kindName} {(_db is null ? "read (dry run)" : "imported")} in {timer.Elapsed.TotalSeconds:F1}s");
    }

    // Replaces the whole table in one transaction. Readers keep seeing the old rows until it commits,
    // and if anything fails the transaction rolls back and the previous data stays in place.
    private async Task<int> ReplaceTableAsync<T>(
        string table, string columns, List<T> items, Func<NpgsqlBinaryImporter, T, Task> writeRow,
        string kind, string fileName, string hash)
    {
        await using var conn = await _db!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var delete = new NpgsqlCommand($"DELETE FROM {table}", conn, tx))
            await delete.ExecuteNonQueryAsync();

        // COPY ... BINARY is Postgres's bulk-load path, far faster than one INSERT per row
        await using (var importer = await conn.BeginBinaryImportAsync($"COPY {table} ({columns}) FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var item in items)
            {
                await importer.StartRowAsync();
                await writeRow(importer, item);
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
            save.Parameters.AddWithValue("kind", kind);
            save.Parameters.AddWithValue("file", fileName);
            save.Parameters.AddWithValue("hash", hash);
            save.Parameters.AddWithValue("rows", items.Count);
            await save.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return items.Count;
    }

    private static async Task WriteMinifigAsync(NpgsqlBinaryImporter importer, LegoMinifig minifig)
    {
        await WriteAsync(importer, minifig.Number, NpgsqlDbType.Text);
        await WriteAsync(importer, minifig.Name ?? "", NpgsqlDbType.Text);
        await WriteAsync(importer, minifig.CategoryId, NpgsqlDbType.Integer);
        await WriteAsync(importer, minifig.CategoryName, NpgsqlDbType.Text);
        await WriteAsync(importer, minifig.YearReleased, NpgsqlDbType.Integer);
        await WriteAsync(importer, minifig.WeightGrams, NpgsqlDbType.Double);
    }

    private static Task WriteAsync<T>(NpgsqlBinaryImporter importer, T? value, NpgsqlDbType type)
        => value is null ? importer.WriteNullAsync() : importer.WriteAsync(value, type);

    private async Task<string?> GetImportedHashAsync(string kind)
    {
        if (_db is null)
            return null;

        await using var cmd = _db.CreateCommand("SELECT file_hash FROM import_state WHERE kind = @kind");
        cmd.Parameters.AddWithValue("kind", kind);
        return await cmd.ExecuteScalarAsync() as string;
    }

    // The name used in import_state, which the app also reads
    public static string KindName(CatalogKind kind) => kind switch
    {
        CatalogKind.Minifigs => "minifigs",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static void Log(string message)
        => Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {message}");
}
