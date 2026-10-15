# BrickLink catalog files

Visibrick's minifig catalog comes from BrickLink's catalog downloads. The `.txt` files in
this folder are not committed to git; download fresh copies whenever you want to update the catalog.

## Updating the catalog

1. Sign in at https://www.bricklink.com/catalogDownload.asp
2. Download **Minifigures**, choosing **Tab-Delimited File**, and save it as `Minifigures.txt`
3. Put it in this folder (`data/bricklink/`). The file name tells the importer what's in it:
   it must contain "minifig".
4. From the repository root:

   ```powershell
   $env:VISIBRICK_DB = "Host=aws-0-us-east-2.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.akzjgaqiummkclplizga;Password=YOUR_DB_PASSWORD;SSL Mode=Require"
   dotnet run --project Visibrick.Sync
   ```

The app notices the new data the next time it checks (on launch, or when it comes back to the foreground,
at most every 6 hours) and downloads it.

## Options

| Command | What it does |
|---|---|
| `dotnet run --project Visibrick.Sync -- --dry-run` | Reads and checks the files only; no database needed |
| `dotnet run --project Visibrick.Sync -- --force` | Re-imports even files that haven't changed |
| `dotnet run --project Visibrick.Sync -- <folder>` | Imports from a different folder |

Files that haven't changed since the last import are skipped. If a file's columns aren't what the
importer expects, it fails with a message listing the columns it found, and the database is left as it was.
