using System.Diagnostics;
using SQLite;
using Visibrick.Core.Models;

namespace Visibrick.Data;

// The app's local copy of the BrickLink catalog (all item types in one table)
public class CatalogRepository
{
    private readonly SQLiteAsyncConnection _db;

    public CatalogRepository(AppDatabase database)
    {
        _db = database.Connection;
    }

    // One page of items of a type whose name or number contains the search text (case-insensitive).
    // Newest first; items with no year (including all parts) are ordered by number.
    public Task<List<CatalogItem>> SearchAsync(string type, string? search, int skip, int take)
        => Filter(type, search)
            .OrderByDescending(x => x.YearReleased)
            .ThenBy(x => x.Number)
            .Skip(skip)
            .Take(take)
            .ToListAsync();

    public Task<CatalogItem?> GetAsync(string type, string number)
        => _db.FindAsync<CatalogItem>(CatalogItem.MakeKey(type, number))!;

    public Task<int> CountAsync(string type, string? search = null)
        => Filter(type, search).CountAsync();

    public Task<int> CountAllAsync()
        => _db.Table<CatalogItem>().CountAsync();

    // Replaces every item of one type in one transaction
    public async Task ReplaceTypeAsync(string type, List<CatalogItem> items)
    {
        await _db.RunInTransactionAsync(conn =>
        {
            conn.Execute("DELETE FROM CatalogItem WHERE Type = ?", type);
            conn.InsertAll(items, runInTransaction: false);
        });

        Debug.WriteLine($"SAVED {items.Count} ITEMS OF TYPE {type}");
    }

    private AsyncTableQuery<CatalogItem> Filter(string type, string? search)
    {
        var query = _db.Table<CatalogItem>().Where(x => x.Type == type);

        if (string.IsNullOrWhiteSpace(search))
            return query;

        // sqlite-net turns Contains into instr(), which is case-sensitive, so compare lowercase on both sides
        var text = search.Trim().ToLowerInvariant();
        return query.Where(x => x.Name!.ToLower().Contains(text) || x.Number.ToLower().Contains(text));
    }
}
