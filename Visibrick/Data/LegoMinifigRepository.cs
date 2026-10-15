using System.Diagnostics;
using SQLite;
using Visibrick.Core.Models;

namespace Visibrick.Data;

public class LegoMinifigRepository
{
    private readonly SQLiteAsyncConnection _db;

    public LegoMinifigRepository(AppDatabase database)
    {
        _db = database.Connection;
    }

    public Task<List<LegoMinifig>> GetAllAsync()
        => _db.Table<LegoMinifig>().ToListAsync();

    // One page of minifigs whose name or number contains the search text (case-insensitive).
    // Newest first; minifigs with an unknown year come last.
    public Task<List<LegoMinifig>> SearchAsync(string? search, int skip, int take)
        => Filter(search)
            .OrderByDescending(x => x.YearReleased)
            .ThenBy(x => x.Number)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

    public Task<int> CountAsync(string? search = null)
        => Filter(search).CountAsync();

    private AsyncTableQuery<LegoMinifig> Filter(string? search)
    {
        var query = _db.Table<LegoMinifig>();

        if (string.IsNullOrWhiteSpace(search))
            return query;

        // sqlite-net turns Contains into instr(), which is case-sensitive, so compare lowercase on both sides
        var text = search.Trim().ToLowerInvariant();
        return query.Where(x => x.Name!.ToLower().Contains(text) || x.Number!.ToLower().Contains(text));
    }

    public Task InsertAsync(LegoMinifig minifig)
        => _db.InsertAsync(minifig);

    public Task InsertOrReplaceAsync(LegoMinifig minifig)
        => _db.InsertOrReplaceAsync(minifig);

    public async Task ReplaceAllAsync(List<LegoMinifig> minifigs)
    {
        await _db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<LegoMinifig>();
            Debug.WriteLine("CLEARED MINIFIGS");

            foreach (var minifig in minifigs)
            {
                conn.Insert(minifig);
            }
        });
    }
}
