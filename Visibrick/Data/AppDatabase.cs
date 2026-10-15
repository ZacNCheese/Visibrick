using SQLite;
using Visibrick.Core.Models;

namespace Visibrick.Data;

public class AppDatabase
{
    private readonly SQLiteAsyncConnection _db;

    public AppDatabase(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);

        // 👇 THIS creates your tables
        _db.CreateTableAsync<CatalogItem>().Wait();

        // Earlier versions kept minifigs and colors in their own tables. This file is only a downloaded copy
        // of the catalog, so the old tables can simply go; everything is now in CatalogItem.
        _db.ExecuteAsync("DROP TABLE IF EXISTS LegoMinifig").Wait();
        _db.ExecuteAsync("DROP TABLE IF EXISTS LegoColor").Wait();
    }

    public SQLiteAsyncConnection Connection => _db;
}
