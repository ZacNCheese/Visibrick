using System.Diagnostics;
using Visibrick.Core.Models;
using Visibrick.Core.Rebrickable;
using Visibrick.Data;

namespace Visibrick.Services.Sync;

public class LegoColorSyncService
{
    private readonly LegoColorRepository _repo;

    public LegoColorSyncService(LegoColorRepository repo)
    {
        _repo = repo;
    }

    public async Task SyncLegoColorsAsync()
    {
        // Stream colors.csv.gz from Rebrickable, then replace every color in the database in one transaction.
        // This file is updated daily (as of June 2026), so we can use it to keep our local database up to date with the latest colors

        using var client = new HttpClient();
        await using var stream = await client.GetStreamAsync(RebrickableCsv.ColorsUrl);

        var colors = new List<LegoColor>();

        await foreach (var cols in RebrickableCsv.ReadRowsAsync(stream))
        {
            if (RebrickableCsv.ParseColor(cols) is { } color)
                colors.Add(color);
        }

        Debug.WriteLine("COLORS PARSED: " + colors.Count);

        await _repo.ReplaceAllAsync(colors);
    }
}
