using SQLite;
using Visibrick.Models;

namespace Visibrick.Data;

// The signed-in user's collection as cached on this device (see CollectionService for syncing with the server)
public class CollectionRepository
{
    private readonly SQLiteAsyncConnection _db;

    public CollectionRepository(AppDatabase database)
    {
        _db = database.Connection;
    }

    public Task<List<CollectionEntry>> GetAllAsync()
        => _db.Table<CollectionEntry>().ToListAsync();

    public async Task ReplaceAllAsync(List<CollectionEntry> entries)
    {
        await _db.RunInTransactionAsync(conn =>
        {
            conn.DeleteAll<CollectionEntry>();
            conn.InsertAll(entries, runInTransaction: false);
        });
    }

    public Task SetAsync(CollectionEntry entry) => _db.InsertOrReplaceAsync(entry);

    public Task RemoveAsync(string key) => _db.DeleteAsync<CollectionEntry>(key);

    public Task ClearAsync() => _db.DeleteAllAsync<CollectionEntry>();

    // Owned items with their catalog details, most recently changed first.
    // type: a CatalogType code, or null for everything. Items missing from the local catalog are left out.
    public Task<List<OwnedItem>> GetOwnedItemsAsync(string? type, string? search)
    {
        var sql = "SELECT c.*, e.Quantity AS Quantity FROM CollectionEntry e JOIN CatalogItem c ON c.Key = e.Key WHERE 1 = 1";
        var args = new List<object>();

        if (type is not null)
        {
            sql += " AND c.Type = ?";
            args.Add(type);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = "%" + search.Trim().ToLowerInvariant() + "%";
            sql += " AND (lower(c.Name) LIKE ? OR lower(c.Number) LIKE ?)";
            args.Add(pattern);
            args.Add(pattern);
        }

        sql += " ORDER BY e.UpdatedAt DESC";
        return _db.QueryAsync<OwnedItem>(sql, args.ToArray());
    }
}
