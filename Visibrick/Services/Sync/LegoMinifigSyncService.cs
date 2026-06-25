using System.Diagnostics;
using VisiBrick;

public class LegoMinifigSyncService
{
    private readonly RestService _api;
    private readonly LegoMinifigsRepository _repo;

    public LegoMinifigSyncService(RestService api, LegoMinifigsRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    public async Task SyncLegoMinifigsAsync()
    {
        Debug.WriteLine("Starting to grab minifigs...");
        var minifigs = await _api.GetLegoMinifigsAsync();
        var lastModified = await _repo.GetLatestModifiedAsync();
        Debug.WriteLine("Last Modified Date Minifigs: " + lastModified);

        foreach (var minifig in minifigs)
        {
            // 👇 if DB is empty OR this record is newer → keep it
            //if (lastModified == null || minifig.LastModified > lastModified?.AddDays(-100)) //for testing
            if (lastModified == null || minifig.LastModified >=lastModified)
            {
                await _repo.InsertOrReplaceAsync(minifig);
                Debug.WriteLine("UPSERTING FIGURE: " + minifig.MinifigName);
            }
        }
    }
}