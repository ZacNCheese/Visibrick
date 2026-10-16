using Npgsql;
using Visibrick.Core.BrickLink;
using Visibrick.Core.Models;
using Visibrick.Sync;
using Visibrick.Sync.Rebrickable;

// Imports the catalog into Postgres (Supabase):
//   - BrickLink catalog downloads (tab-delimited .txt files) in data/bricklink/: minifigs, gear and books.
//     Download them from https://www.bricklink.com/catalogDownload.asp
//   - Rebrickable's all_downloads.zip in data/rebrickable/: sets, parts, and what's in each set.
//     Download it from https://rebrickable.com/downloads
//
//   dotnet run --project Visibrick.Sync                    import both folders into the database in VISIBRICK_DB
//   dotnet run --project Visibrick.Sync -- <folder>        use a different BrickLink folder
//   dotnet run --project Visibrick.Sync -- --dry-run       read and check the files only, no database needed
//   dotnet run --project Visibrick.Sync -- --force         re-import even if a file hasn't changed

var dryRun = args.Contains("--dry-run");
var force = args.Contains("--force");
var brickLinkFolder = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine("data", "bricklink");
var rebrickableFolder = Path.Combine("data", "rebrickable");

if (!Directory.Exists(brickLinkFolder) && !Directory.Exists(rebrickableFolder))
{
    CatalogImporter.Log($"Neither {Path.GetFullPath(brickLinkFolder)} nor {Path.GetFullPath(rebrickableFolder)} exists.");
    CatalogImporter.Log("Run this from the repository root, or pass the folder that holds the BrickLink downloads.");
    return 1;
}

// BrickLink downloads in the folder; anything else is listed and ignored. Sets and parts now come from
// Rebrickable, but BrickLink's files for them (if present) are still read to check Rebrickable's BrickLink numbers.
var brickLinkFiles = new List<(string Path, CatalogType Type)>();
foreach (var path in Directory.Exists(brickLinkFolder) ? Directory.GetFiles(brickLinkFolder, "*.txt").Order() : Enumerable.Empty<string>())
{
    if (CatalogType.FromFileName(path) is { } type)
        brickLinkFiles.Add((path, type));
    else
        CatalogImporter.Log($"{Path.GetFileName(path)}: not a supported download (expected Minifigures, Sets, Parts, Gear or Books), ignored");
}

// The newest Rebrickable zip, if there is one
var rebrickableZip = Directory.Exists(rebrickableFolder)
    ? Directory.GetFiles(rebrickableFolder, "*.zip").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
    : null;

if (brickLinkFiles.Count == 0 && rebrickableZip is null)
{
    CatalogImporter.Log("Nothing to import: no BrickLink downloads in data/bricklink and no zip in data/rebrickable.");
    return 1;
}

// Matches docker-compose.yml; set VISIBRICK_DB to point at a hosted database (e.g. Supabase)
var connectionString = Environment.GetEnvironmentVariable("VISIBRICK_DB")
    ?? "Host=localhost;Port=5432;Database=visibrick;Username=visibrick;Password=visibrick";

await using var db = dryRun ? null : NpgsqlDataSource.Create(connectionString);
var importer = new CatalogImporter(db, force);

try
{
    await importer.EnsureSchemaAsync();
}
catch (Exception ex)
{
    CatalogImporter.Log($"Can't reach the database: {ex.Message}");
    return 1;
}

// Each import is its own transaction, so one failing doesn't undo the others
var failed = false;
foreach (var (path, type) in brickLinkFiles)
{
    if (type.FromRebrickable)
    {
        CatalogImporter.Log($"{Path.GetFileName(path)}: {type.CountNoun} now come from Rebrickable; only used to check BrickLink numbers");
        continue;
    }

    try
    {
        await importer.ImportAsync(path, type);
    }
    catch (Exception ex)
    {
        CatalogImporter.Log($"{Path.GetFileName(path)}: FAILED - {ex.Message}");
        failed = true;
    }
}

if (rebrickableZip is null)
    CatalogImporter.Log($"No Rebrickable zip in {Path.GetFullPath(rebrickableFolder)}; sets and parts not updated");
else
{
    try
    {
        var brickLinkNumbers = new BrickLinkNumbers(
            await NumbersAsync(CatalogType.Sets),
            await NumbersAsync(CatalogType.Parts),
            await NumbersAsync(CatalogType.Minifigs));

        await new RebrickableImporter(db, force, brickLinkNumbers).ImportAsync(rebrickableZip);
    }
    catch (Exception ex)
    {
        CatalogImporter.Log($"{Path.GetFileName(rebrickableZip)}: FAILED - {ex.Message}");
        failed = true;
    }
}

try
{
    await importer.RefreshCategoriesAsync();
}
catch (Exception ex)
{
    CatalogImporter.Log($"Updating the category list FAILED - {ex.Message}");
    failed = true;
}

// A non-zero exit code lets a scheduler or CI job flag the run as failed
return failed ? 1 : 0;

// Every item number in one BrickLink download, or null if it isn't in the folder
async Task<HashSet<string>?> NumbersAsync(CatalogType type)
{
    var path = brickLinkFiles.FirstOrDefault(f => f.Type == type).Path;
    if (path is null)
        return null;

    var file = await BrickLinkCatalogFile.ReadAsync(path, type);
    return file.ParseItems().Select(i => i.Number).ToHashSet();
}
