using System.Diagnostics;
using LEGOOO;
using System.Globalization;
using System.IO.Compression;
using Microsoft.Data.Sqlite;

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

    

    public async Task SyncColorsAsync(string dbPath)
    {
        var url = "https://cdn.rebrickable.com/media/downloads/colors.csv.gz";

        var tempDir = Path.Combine(FileSystem.CacheDirectory, "rebrickable_colors");
        Directory.CreateDirectory(tempDir);

        var gzPath = Path.Combine(tempDir, "colors.csv.gz");
        var csvPath = Path.Combine(tempDir, "colors.csv");

        try
        {
            // -------------------------
            // 1. DOWNLOAD FILE
            // -------------------------
            using (var client = new HttpClient())
            using (var response = await client.GetAsync(url))
            {
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(gzPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fs);
            }

            // -------------------------
            // 2. DECOMPRESS GZ -> CSV
            // -------------------------
            await using (var gzStream = File.OpenRead(gzPath))
            using (var decompressionStream = new GZipStream(gzStream, CompressionMode.Decompress))
            await using (var outFile = File.Create(csvPath))
            {
                await decompressionStream.CopyToAsync(outFile);
            }

            // -------------------------
            // 3. OPEN SQLITE
            // -------------------------
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var tx = conn.BeginTransaction();

            // -------------------------
            // 4. DELETE OLD DATA
            // -------------------------
            var deleteCmd = conn.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM LegoColor";
            deleteCmd.ExecuteNonQuery();

            // -------------------------
            // 5. PREPARE INSERT
            // -------------------------
            var insertCmd = conn.CreateCommand();
            insertCmd.CommandText =
                @"INSERT INTO LegoColor (Id, Name, Rgb, IsTrans)
                VALUES ($id, $name, $rgb, $isTrans)";

            var pId = insertCmd.CreateParameter(); pId.ParameterName = "$id";
            var pName = insertCmd.CreateParameter(); pName.ParameterName = "$name";
            var pRgb = insertCmd.CreateParameter(); pRgb.ParameterName = "$rgb";
            var pTrans = insertCmd.CreateParameter(); pTrans.ParameterName = "$isTrans";

            insertCmd.Parameters.AddRange(new[]
            {
                pId, pName, pRgb, pTrans
            });

            // -------------------------
            // 6. READ CSV + INSERT
            // -------------------------
            var lines = await File.ReadAllLinesAsync(csvPath);

            for (int i = 1; i < lines.Length; i++) // skip header
            {
                var cols = lines[i].Split(',');

                if (cols.Length < 4)
                    continue;

                pId.Value = int.Parse(cols[0], CultureInfo.InvariantCulture);
                pName.Value = cols[1];
                pRgb.Value = cols[2];
                pTrans.Value = cols[3] == "t" ? 1 : 0;

                insertCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        finally
        {
            // -------------------------
            // 7. CLEANUP TEMP FILES
            // -------------------------
            try
            {
                if (File.Exists(gzPath))
                    File.Delete(gzPath);

                if (File.Exists(csvPath))
                    File.Delete(csvPath);

                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}