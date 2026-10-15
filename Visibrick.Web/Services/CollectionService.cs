using System.Text.Json.Serialization;
using Visibrick.Core.Models;

namespace Visibrick.Web.Services;

// The signed-in user's collection, stored in Supabase (collection_items). Keeps a type:number → quantity
// lookup in memory so every catalog card can show its badge without asking the server.
// Changes show immediately and are then saved; if saving fails, the change is undone and the error passed on.
public class CollectionService
{
    private const int PageSize = 1000;

    private readonly AuthService _auth;
    private readonly SupabaseRest _rest;
    private Dictionary<string, int> _quantities = new();
    private string? _loadedUserId;
    private Task? _loading;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public CollectionService(AuthService auth, SupabaseRest rest)
    {
        _auth = auth;
        _rest = rest;
        _auth.Changed += () => _ = LoadForCurrentUserAsync();
    }

    // The collection was loaded, cleared, or an item's quantity changed
    public event Action? Changed;

    public int QuantityOf(string type, string number)
        => _quantities.GetValueOrDefault(CatalogItem.MakeKey(type, number));

    // Makes sure the signed-in user's collection is loaded (safe to call from every page)
    public Task EnsureLoadedAsync() => LoadForCurrentUserAsync();

    public Task AddOneAsync(string type, string number) => SetQuantityAsync(type, number, QuantityOf(type, number) + 1);

    // Sets how many the user owns; 0 removes it
    public async Task SetQuantityAsync(string type, string number, int quantity)
    {
        var user = _auth.CurrentUser ?? throw new AppException("Sign in to start your collection.");
        var key = CatalogItem.MakeKey(type, number);

        await _writeGate.WaitAsync();
        try
        {
            var previous = QuantityOf(type, number);
            if (previous == quantity)
                return;

            Apply(key, quantity);
            try
            {
                var token = await _auth.GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");

                if (quantity > 0)
                {
                    using var _ = await _rest.SendAsync(HttpMethod.Post, "rest/v1/collection_items?on_conflict=user_id,item_type,item_number",
                        new { user_id = user.Id, item_type = type, item_number = number, quantity, updated_at = DateTimeOffset.UtcNow },
                        token, prefer: "resolution=merge-duplicates,return=minimal");
                }
                else
                {
                    using var _ = await _rest.SendAsync(HttpMethod.Delete,
                        $"rest/v1/collection_items?item_type=eq.{Uri.EscapeDataString(type)}&item_number=eq.{CatalogService.Quote(number)}", null, token);
                }
            }
            catch
            {
                // Saving failed: put it back so the screen matches the server
                Apply(key, previous);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // One page of the user's collection with catalog details, most recently changed first
    public async Task<(List<OwnedItem> Rows, int Total)> SearchAsync(string? type, string? search, int skip, int take)
    {
        var token = await _auth.GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
        var path = "rest/v1/my_collection?select=*" +
            (type is null ? "" : $"&item_type=eq.{Uri.EscapeDataString(type)}") +
            CatalogService.SearchFilter(search, "name", "item_number") +
            $"&order=updated_at.desc&limit={take}&offset={skip}";

        return await _rest.GetPageAsync<OwnedItem>(path, token);
    }

    // Totals for the summary and filter chips: (different items, total pieces) per type code
    public Dictionary<string, (int Items, int Pieces)> CountsByType()
        => _quantities
            .GroupBy(q => q.Key.Split(':', 2)[0])
            .ToDictionary(g => g.Key, g => (g.Count(), g.Sum(q => q.Value)));

    private Task LoadForCurrentUserAsync()
    {
        var userId = _auth.CurrentUser?.Id;

        // Token refreshes also raise Changed; only reload when the user actually changes
        if (userId == _loadedUserId && _loading is not null)
            return _loading;

        _loadedUserId = userId;
        return _loading = LoadAsync(userId);
    }

    private async Task LoadAsync(string? userId)
    {
        if (userId is null)
        {
            _quantities = new();
            Changed?.Invoke();
            return;
        }

        try
        {
            var token = await _auth.GetAccessTokenAsync();
            var rows = new List<Row>();
            while (token is not null)
            {
                var page = await _rest.GetAsync<Row>($"rest/v1/collection_items?select=item_type,item_number,quantity&order=id&limit={PageSize}&offset={rows.Count}", token);
                rows.AddRange(page);
                if (page.Count < PageSize)
                    break;
            }

            _quantities = rows.ToDictionary(r => CatalogItem.MakeKey(r.ItemType, r.ItemNumber), r => r.Quantity);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Loading the collection failed: " + ex.Message);
        }

        Changed?.Invoke();
    }

    private void Apply(string key, int quantity)
    {
        if (quantity > 0)
            _quantities[key] = quantity;
        else
            _quantities.Remove(key);

        Changed?.Invoke();
    }

    private class Row
    {
        [JsonPropertyName("item_type")]
        public string ItemType { get; set; } = "";

        [JsonPropertyName("item_number")]
        public string ItemNumber { get; set; } = "";

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }
    }
}

// A row of the my_collection view: an owned item with its catalog details
public class OwnedItem
{
    [JsonPropertyName("item_type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("item_number")]
    public string Number { get; set; } = "";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("category_name")]
    public string? CategoryName { get; set; }

    [JsonPropertyName("year_released")]
    public int? YearReleased { get; set; }

    // The same item as a catalog item, for the shared item card
    public CatalogItem ToCatalogItem() => new()
    {
        Type = Type,
        Number = Number,
        Name = Name ?? Number,
        CategoryName = CategoryName,
        YearReleased = YearReleased
    };
}
