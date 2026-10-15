using System.Diagnostics;
using Visibrick.Core.Models;
using Visibrick.Data;

namespace Visibrick.Services.Sync;

// Copies the catalog from Supabase into the app's local SQLite database, so the app works offline.
// Each item type is only downloaded when Visibrick.Sync has imported a new BrickLink file for it since last time.
public class CatalogSyncService
{
    private readonly SupabaseCatalogClient _client;
    private readonly CatalogRepository _repo;

    public CatalogSyncService(SupabaseCatalogClient client, CatalogRepository repo)
    {
        _client = client;
        _repo = repo;
    }

    // Returns true if anything changed
    public async Task<bool> SyncAsync()
    {
        var imports = await _client.GetImportStateAsync();
        var changed = false;

        foreach (var type in CatalogType.All)
            changed |= await SyncTypeAsync(type, imports);

        return changed;
    }

    private async Task<bool> SyncTypeAsync(CatalogType type, Dictionary<string, SupabaseCatalogClient.ImportState> imports)
    {
        // Nothing imported on the server yet for this type
        if (!imports.TryGetValue(type.Code, out var import))
            return false;

        // Same file as last time, and the local copy is still there
        var hashKey = "catalog-hash:" + type.Code;
        if (Preferences.Get(hashKey, null) == import.FileHash && await _repo.CountAsync(type.Code) > 0)
        {
            Debug.WriteLine($"{type.Name.ToUpperInvariant()} UNCHANGED");
            return false;
        }

        var timer = Stopwatch.StartNew();
        var items = await _client.GetItemsAsync(type.Code, import.RowCount);
        await _repo.ReplaceTypeAsync(type.Code, items);
        Debug.WriteLine($"{type.Name.ToUpperInvariant()} DOWNLOADED: {items.Count} in {timer.Elapsed.TotalSeconds:F1}s");

        // Remember the file only after its items are safely saved, so a failed download is retried next time
        Preferences.Set(hashKey, import.FileHash);
        return true;
    }
}
