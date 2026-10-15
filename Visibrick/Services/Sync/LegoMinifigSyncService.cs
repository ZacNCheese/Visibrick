using System.Diagnostics;
using Visibrick.Core.Models;
using Visibrick.Core.Rebrickable;
using Visibrick.Data;

namespace Visibrick.Services.Sync;

public class LegoMinifigSyncService
{
    private readonly LegoMinifigRepository _repo;

    public LegoMinifigSyncService(LegoMinifigRepository repo)
    {
        _repo = repo;
    }

    public async Task SyncLegoMinifigsAsync()
    {
        // Stream minifigs.csv.gz from Rebrickable, then replace every minifig in the database in one transaction.
        // Names can contain commas, so those are wrapped in quotes; RebrickableCsv handles that.

        using var client = new HttpClient();
        await using var stream = await client.GetStreamAsync(RebrickableCsv.MinifigsUrl);

        var minifigs = new List<LegoMinifig>();

        await foreach (var cols in RebrickableCsv.ReadRowsAsync(stream))
        {
            if (RebrickableCsv.ParseMinifig(cols) is { } minifig)
                minifigs.Add(minifig);
        }

        Debug.WriteLine("MINIFIGS PARSED: " + minifigs.Count);

        await _repo.ReplaceAllAsync(minifigs);
    }
}
