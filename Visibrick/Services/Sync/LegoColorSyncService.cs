using System.Diagnostics;
using VisiBrick;
using System.Globalization;
using System.IO.Compression;
//using Microsoft.Data.Sqlite;

public class LegoColorSyncService
{
    private readonly RestService _api;
    private readonly LegoColorRepository _repo;

    public LegoColorSyncService(RestService api, LegoColorRepository repo)
    {
        _api = api;
        _repo = repo;
    }

    /*public async Task SyncLegoColorsAsync()
    {
        var colors = await _api.GetLegoColorsAsync();

        foreach (var color in colors)
        {
            Debug.WriteLine("UPSERTING COLOR: " + color.ColorName);
            await _repo.InsertOrReplaceAsync(color);
        }
    }*/

    

    public async Task SyncLegoColorsAsync()
    {
        var url = "https://cdn.rebrickable.com/media/downloads/colors.csv.gz?1782285120.1112702";

        var tempDir = Path.Combine(FileSystem.CacheDirectory, "rebrickable_colors");
        Directory.CreateDirectory(tempDir);

        var gzPath = Path.Combine(tempDir, "colors.csv.gz");
        var csvPath = Path.Combine(tempDir, "colors.csv");

        try
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(gzPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fs);
                Debug.WriteLine("Downloaded Colors");
            }

            await using (var gzStream = File.OpenRead(gzPath))
            using (var decompressionStream = new GZipStream(gzStream, CompressionMode.Decompress))
            await using (var outFile = File.Create(csvPath))
            {
                await decompressionStream.CopyToAsync(outFile);
            }

            var colors = new List<LegoColor>();

            var lines = await File.ReadAllLinesAsync(csvPath);

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = lines[i].Split(',');

                if (cols.Length < 4)
                    continue;

                colors.Add(new LegoColor
                {
                    Id = int.Parse(cols[0], CultureInfo.InvariantCulture),
                    ColorName = cols[1],
                    ColorRGB = cols[2],
                    Is_Transluscent = bool.TryParse(cols[3], out bool isTrans) && isTrans
                });
            }

            Debug.WriteLine("FULL COLORS LIST: " + colors.ToString());

            await _repo.ReplaceAllAsync(colors);
            
        }
        finally
        {
            if (File.Exists(gzPath)) File.Delete(gzPath);
            if (File.Exists(csvPath)) File.Delete(csvPath);
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Debug.WriteLine("DELETED FILES");
        }
    }
}