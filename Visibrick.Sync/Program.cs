using Npgsql;
using Visibrick.Core.BrickLink;
using Visibrick.Sync;

// Imports BrickLink catalog downloads (tab-delimited .txt files) into Postgres (Supabase).
// Download the files from https://www.bricklink.com/catalogDownload.asp and put them in data/bricklink/.
//
//   dotnet run --project Visibrick.Sync                    import data/bricklink into the database in VISIBRICK_DB
//   dotnet run --project Visibrick.Sync -- <folder>        import a different folder
//   dotnet run --project Visibrick.Sync -- --dry-run       read and check the files only, no database needed
//   dotnet run --project Visibrick.Sync -- --force         re-import even if a file hasn't changed

var dryRun = args.Contains("--dry-run");
var force = args.Contains("--force");
var folder = args.FirstOrDefault(a => !a.StartsWith("--")) ?? Path.Combine("data", "bricklink");

if (!Directory.Exists(folder))
{
    CatalogImporter.Log($"Folder not found: {Path.GetFullPath(folder)}");
    CatalogImporter.Log("Run this from the repository root, or pass the folder that holds the BrickLink downloads.");
    return 1;
}

// Which supported downloads are in the folder; anything else is listed and ignored
var files = new List<(string Path, CatalogKind Kind)>();
foreach (var path in Directory.GetFiles(folder, "*.txt").OrderBy(p => p))
{
    if (BrickLinkCatalogFile.KindFromFileName(path) is { } kind)
        files.Add((path, kind));
    else
        CatalogImporter.Log($"{Path.GetFileName(path)}: not a supported download (expected Minifigures), ignored");
}

if (files.Count == 0)
{
    CatalogImporter.Log($"No BrickLink downloads found in {Path.GetFullPath(folder)}");
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

// Each file is its own transaction, so one failing doesn't undo the others
var failed = false;
foreach (var (path, kind) in files)
{
    try
    {
        await importer.ImportAsync(path, kind);
    }
    catch (Exception ex)
    {
        CatalogImporter.Log($"{Path.GetFileName(path)}: FAILED - {ex.Message}");
        failed = true;
    }
}

// A non-zero exit code lets a scheduler or CI job flag the run as failed
return failed ? 1 : 0;
