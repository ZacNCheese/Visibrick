# Rebrickable data

Visibrick's **sets and parts**, and **what's inside each set** (parts, minifigs, sets within sets), come from
Rebrickable's free downloads. Minifigs stay on BrickLink; Rebrickable's links between its numbers and
BrickLink's tie them together (e.g. Rebrickable fig-012420 is BrickLink sh0810).

The zip in this folder is not committed to git.

## Updating

1. Go to https://rebrickable.com/downloads and download **all the files as one zip** (`all_downloads.zip`).
2. Put it in this folder (`data/rebrickable/`), replacing the old one. If there are several zips, the newest is used.
3. From the repository root, the same command as the BrickLink import (it imports both):

   ```powershell
   $env:VISIBRICK_DB = "Host=aws-0-us-east-2.pooler.supabase.com;Port=5432;Database=postgres;Username=postgres.akzjgaqiummkclplizga;Password=YOUR_DB_PASSWORD;SSL Mode=Require"
   dotnet run --project Visibrick.Sync
   ```

An unchanged zip is skipped. `--dry-run` reads and checks it without a database; `--force` re-imports it.

## What gets imported

| From the zip | Into Supabase |
|---|---|
| `sets.csv`, `themes.csv` | `catalog_items` (type S). Merchandise with no pieces (bags, keychains, clothing...) is left out. |
| `parts.csv`, `part_categories.csv` | `catalog_items` (type P) |
| `colors.csv`, `color_mappings.csv` | `colors` |
| `inventories.csv`, `inventory_parts.csv` | `set_parts`, `minifig_parts`, `part_colors` (each set's newest inventory) |
| `inventory_minifigs.csv`, `minifigs.csv` | `set_minifigs` |
| `inventory_sets.csv` | `set_subsets` |
| `set_mappings.csv`, `part_mappings.csv` | BrickLink numbers for sets, parts and minifigs |

`elements.csv` and `part_relationships.csv` aren't used yet.

The whole import runs in one transaction: if anything fails, the previous data stays. It takes up about
250 MB of the database (Supabase's free plan allows 500 MB), most of it `set_parts`.
