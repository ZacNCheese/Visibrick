using SQLite;

public class LegoColorRepository
{
    private readonly SQLiteAsyncConnection _db;

    public LegoColorRepository(AppDatabase database)
    {
        _db = database.Connection;
    }

    public Task<List<LegoColor>> GetAllAsync()
        => _db.Table<LegoColor>().ToListAsync();

    public Task InsertAsync(LegoColor color)
        => _db.InsertAsync(color);

    public Task InsertOrReplaceAsync(LegoColor color)
        => _db.InsertOrReplaceAsync(color);
}