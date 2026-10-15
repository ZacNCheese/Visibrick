using SQLite;
using Visibrick.Models;

namespace Visibrick.Data;

public class AppDatabase
{
    private readonly SQLiteAsyncConnection _db;

    public AppDatabase(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);

        // 👇 THIS creates your tables
        _db.CreateTableAsync<LegoColor>().Wait();
        _db.CreateTableAsync<LegoMinifig>().Wait();
    }

    public SQLiteAsyncConnection Connection => _db;
}
