using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Npgsql;
using Visibrick.Core.Rebrickable;

namespace Visibrick.Sync;

public class CatalogSync
{
    private readonly HttpClient _http;
    private readonly NpgsqlDataSource? _db;
    private readonly bool _force;

    // db is null for a dry run: download and parse only, nothing is written
    public CatalogSync(HttpClient http, NpgsqlDataSource? db, bool force)
    {
        _http = http;
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

    public async Task SyncAsync(CatalogFile file)
    {
        var timer = Stopwatch.StartNew();

        var request = new HttpRequestMessage(HttpMethod.Get, file.Url);

        // Ask the CDN to answer 304 Not Modified if the file hasn't changed since our last import
        var lastEtag = _force ? null : await GetLastEtagAsync(file);
        if (lastEtag is not null)
            request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(lastEtag));

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            Log($"{file.Name}: unchanged since last sync, skipped");
            return;
        }

        response.EnsureSuccessStatusCode();
        var etag = response.Headers.ETag?.ToString();

        await using var stream = await response.Content.ReadAsStreamAsync();

        var rows = _db is null
            ? await CountRowsAsync(file, stream)
            : await ImportAsync(file, stream, etag);

        Log($"{file.Name}: {rows:N0} rows {(_db is null ? "parsed (dry run)" : "imported")} in {timer.Elapsed.TotalSeconds:F1}s");
    }

    // Replaces the whole table in one transaction. Readers keep seeing the old rows until it commits,
    // and if anything fails the transaction rolls back and yesterday's data stays in place.
    private async Task<int> ImportAsync(CatalogFile file, Stream stream, string? etag)
    {
        await using var conn = await _db!.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await using (var delete = new NpgsqlCommand($"DELETE FROM {file.Table}", conn, tx))
            await delete.ExecuteNonQueryAsync();

        int rows = 0;

        // COPY ... BINARY is Postgres's bulk-load path, far faster than one INSERT per row
        await using (var importer = await conn.BeginBinaryImportAsync(
            $"COPY {file.Table} ({file.Columns}) FROM STDIN (FORMAT BINARY)"))
        {
            await foreach (var cols in RebrickableCsv.ReadRowsAsync(stream))
            {
                if (await file.WriteRowAsync(importer, cols))
                    rows++;
            }

            await importer.CompleteAsync();
        }

        await using (var save = new NpgsqlCommand("""
            INSERT INTO sync_state (file_name, etag, row_count, synced_at)
            VALUES (@file, @etag, @rows, now())
            ON CONFLICT (file_name) DO UPDATE
            SET etag = excluded.etag, row_count = excluded.row_count, synced_at = excluded.synced_at
            """, conn, tx))
        {
            save.Parameters.AddWithValue("file", file.Name);
            save.Parameters.AddWithValue("etag", (object?)etag ?? DBNull.Value);
            save.Parameters.AddWithValue("rows", rows);
            await save.ExecuteNonQueryAsync();
        }

        await tx.CommitAsync();
        return rows;
    }

    private static async Task<int> CountRowsAsync(CatalogFile file, Stream stream)
    {
        int rows = 0;

        await foreach (var cols in RebrickableCsv.ReadRowsAsync(stream))
        {
            var parsed = file switch
            {
                ColorsFile => RebrickableCsv.ParseColor(cols) is not null,
                MinifigsFile => RebrickableCsv.ParseMinifig(cols) is not null,
                _ => true
            };

            if (parsed)
                rows++;
        }

        return rows;
    }

    private async Task<string?> GetLastEtagAsync(CatalogFile file)
    {
        if (_db is null)
            return null;

        await using var cmd = _db.CreateCommand("SELECT etag FROM sync_state WHERE file_name = @file");
        cmd.Parameters.AddWithValue("file", file.Name);
        return await cmd.ExecuteScalarAsync() as string;
    }

    public static void Log(string message)
        => Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z] {message}");
}
