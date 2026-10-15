using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using Visibrick.Data;
using Visibrick.Models;

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
        // Download the colors.csv.gz file from Rebrickable, Delete the existing colors in the database, and insert the new colors from the CSV file. After all data is inserted, delete the downloaded files and the temporary directory.

        // This file is updated daily (as of June 2026), so we can use it to keep our local database up to date with the latest colors

        var url = "https://cdn.rebrickable.com/media/downloads/colors.csv.gz";

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
                    Name = cols[1],
                    Rgb = cols[2],
                    IsTranslucent = bool.TryParse(cols[3], out bool isTrans) && isTrans
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
            Debug.WriteLine("DELETED COLOR FILES");
        }
    }
}
