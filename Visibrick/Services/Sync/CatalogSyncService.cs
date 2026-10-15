using System.Diagnostics;
using Visibrick.Core.Models;
using Visibrick.Data;

namespace Visibrick.Services.Sync;

// Copies the catalog from Supabase into the app's local SQLite database, so the app works offline.
// Each table is only downloaded when Visibrick.Sync has imported a new BrickLink file since last time.
public class CatalogSyncService
{
    private readonly SupabaseCatalogClient _client;
    private readonly LegoMinifigRepository _minifigRepo;

    public CatalogSyncService(SupabaseCatalogClient client, LegoMinifigRepository minifigRepo)
    {
        _client = client;
        _minifigRepo = minifigRepo;
    }

    // Returns true if anything changed
    public async Task<bool> SyncAsync()
    {
        var imports = await _client.GetImportStateAsync();

        return await SyncTableAsync("minifigs", imports, () => _minifigRepo.CountAsync(), async count =>
            await _minifigRepo.ReplaceAllAsync(await _client.GetAllAsync<LegoMinifig>("minifigs", "number", count)));
    }

    private static async Task<bool> SyncTableAsync(
        string kind,
        Dictionary<string, SupabaseCatalogClient.ImportState> imports,
        Func<Task<int>> localCount,
        Func<int, Task> downloadAndReplace)
    {
        // Nothing imported on the server yet (no BrickLink file for this catalog)
        if (!imports.TryGetValue(kind, out var import))
            return false;

        // Same file as last time, and the local copy is still there
        var hashKey = "catalog-hash:" + kind;
        if (Preferences.Get(hashKey, null) == import.FileHash && await localCount() > 0)
        {
            Debug.WriteLine($"{kind.ToUpperInvariant()} UNCHANGED");
            return false;
        }

        await downloadAndReplace(import.RowCount);
        Debug.WriteLine($"{kind.ToUpperInvariant()} DOWNLOADED: {import.RowCount}");

        // Remember the file only after its rows are safely saved, so a failed download is retried next time
        Preferences.Set(hashKey, import.FileHash);
        return true;
    }
}
