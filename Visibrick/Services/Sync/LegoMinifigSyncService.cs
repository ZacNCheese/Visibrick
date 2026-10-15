using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Visibrick.Data;
using Visibrick.Models;

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
        // Download the minifigs.csv.gz file from Rebrickable, Delete the existing minifigs in the database, and insert the new minifigs from the CSV file. After all data is inserted, delete the downloaded files and the temporary directory.

        // Columns: fig_num,name,num_parts,img_url
        // Names can contain commas, so those are wrapped in quotes, e.g. fig-000003,"Assassin Droid, White",8,...

        var url = "https://cdn.rebrickable.com/media/downloads/minifigs.csv.gz";

        var tempDir = Path.Combine(FileSystem.CacheDirectory, "rebrickable_minifigs");
        Directory.CreateDirectory(tempDir);

        var gzPath = Path.Combine(tempDir, "minifigs.csv.gz");
        var csvPath = Path.Combine(tempDir, "minifigs.csv");

        try
        {
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(gzPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fs);
                Debug.WriteLine("Downloaded Minifigs");
            }

            await using (var gzStream = File.OpenRead(gzPath))
            using (var decompressionStream = new GZipStream(gzStream, CompressionMode.Decompress))
            await using (var outFile = File.Create(csvPath))
            {
                await decompressionStream.CopyToAsync(outFile);
            }

            var minifigs = new List<LegoMinifig>();

            var lines = await File.ReadAllLinesAsync(csvPath);

            for (int i = 1; i < lines.Length; i++)
            {
                var cols = SplitCsvLine(lines[i]);

                if (cols.Count < 4)
                    continue;

                minifigs.Add(new LegoMinifig
                {
                    Number = cols[0],
                    Name = cols[1],
                    NumParts = int.Parse(cols[2], CultureInfo.InvariantCulture),
                    ImageUrl = cols[3]
                });
            }

            Debug.WriteLine("MINIFIGS PARSED: " + minifigs.Count);

            await _repo.ReplaceAllAsync(minifigs);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            Debug.WriteLine("DELETED MINIFIG FILES");
        }
    }

    // Splits one CSV line, keeping commas inside quoted fields and turning "" into "
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];

            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }
}
