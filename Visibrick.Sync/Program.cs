using Npgsql;
using Visibrick.Sync;

// Downloads Rebrickable's daily CSV dumps into Postgres. Meant to run on a schedule (daily, ~09:00 UTC).
//
//   dotnet run --project Visibrick.Sync              sync into the database in VISIBRICK_DB
//   dotnet run --project Visibrick.Sync -- --dry-run download and parse only, no database needed
//   dotnet run --project Visibrick.Sync -- --force   re-import even if a file hasn't changed

var dryRun = args.Contains("--dry-run");
var force = args.Contains("--force");

// Matches docker-compose.yml; set VISIBRICK_DB to point at a hosted database
var connectionString = Environment.GetEnvironmentVariable("VISIBRICK_DB")
    ?? "Host=localhost;Port=5432;Database=visibrick;Username=visibrick;Password=visibrick";

CatalogFile[] files = [new ColorsFile(), new MinifigsFile()];

using var http = new HttpClient();
await using var db = dryRun ? null : NpgsqlDataSource.Create(connectionString);

var sync = new CatalogSync(http, db, force);
var failed = false;

try
{
    await sync.EnsureSchemaAsync();
}
catch (Exception ex)
{
    CatalogSync.Log($"Can't reach the database: {ex.Message}");
    return 1;
}

// Each file is its own transaction, so one failing doesn't undo the others
foreach (var file in files)
{
    try
    {
        await sync.SyncAsync(file);
    }
    catch (Exception ex)
    {
        CatalogSync.Log($"{file.Name}: FAILED - {ex.Message}");
        failed = true;
    }
}

// A non-zero exit code lets the scheduler flag the run as failed
return failed ? 1 : 0;
