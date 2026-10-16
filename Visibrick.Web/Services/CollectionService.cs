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

    // Missing pieces in owned sets and minifigs: item key → (part, color) → how many are missing.
    // Only missing pieces are stored; anything not listed is complete.
    private Dictionary<string, Dictionary<(string Part, int Color), int>> _missing = new();
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

            // Removing an item also removes its missing pieces (the database does the same through its foreign key)
            var missingBefore = _missing.GetValueOrDefault(key);
            if (quantity == 0)
                _missing.Remove(key);

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
                if (missingBefore is not null)
                    _missing[key] = missingBefore;
                Apply(key, previous);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // ---------------------------------------------------------------- missing pieces

    // How many of one part (in one color) are missing from an owned set or minifig
    public int MissingOf(string type, string number, string part, int color)
        => _missing.GetValueOrDefault(CatalogItem.MakeKey(type, number))?.GetValueOrDefault((part, color)) ?? 0;

    // How many pieces in total are missing from an owned set or minifig
    public int MissingTotal(string type, string number)
        => _missing.GetValueOrDefault(CatalogItem.MakeKey(type, number))?.Values.Sum() ?? 0;

    // How many owned items are missing at least one piece
    public int ItemsWithMissing => _missing.Count(m => m.Value.Count > 0);

    // Sets how many of one part are missing (0 = you have them all). The item must be in the collection.
    public async Task SetMissingAsync(string type, string number, string part, int color, int missing)
    {
        var key = CatalogItem.MakeKey(type, number);

        await _writeGate.WaitAsync();
        try
        {
            var previous = MissingOf(type, number, part, color);
            if (previous == missing)
                return;

            ApplyMissing(key, part, color, missing);
            try
            {
                var token = await _auth.GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
                var filter = $"item_type=eq.{Uri.EscapeDataString(type)}&item_number=eq.{CatalogService.Quote(number)}" +
                             $"&part_num=eq.{CatalogService.Quote(part)}&color_id=eq.{color}";

                if (missing > 0)
                {
                    using var _ = await _rest.SendAsync(HttpMethod.Post,
                        "rest/v1/collection_missing_parts?on_conflict=user_id,item_type,item_number,part_num,color_id",
                        new { item_type = type, item_number = number, part_num = part, color_id = color, missing, updated_at = DateTimeOffset.UtcNow },
                        token, prefer: "resolution=merge-duplicates,return=minimal");
                }
                else
                {
                    using var _ = await _rest.SendAsync(HttpMethod.Delete, "rest/v1/collection_missing_parts?" + filter, null, token);
                }
            }
            catch
            {
                ApplyMissing(key, part, color, previous);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // Marks every piece of an owned set or minifig as found
    public async Task ClearMissingAsync(string type, string number)
    {
        var key = CatalogItem.MakeKey(type, number);

        await _writeGate.WaitAsync();
        try
        {
            if (!_missing.Remove(key, out var before))
                return;

            Changed?.Invoke();
            try
            {
                var token = await _auth.GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
                using var _ = await _rest.SendAsync(HttpMethod.Delete,
                    $"rest/v1/collection_missing_parts?item_type=eq.{Uri.EscapeDataString(type)}&item_number=eq.{CatalogService.Quote(number)}", null, token);
            }
            catch
            {
                _missing[key] = before;
                Changed?.Invoke();
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void ApplyMissing(string key, string part, int color, int missing)
    {
        if (missing > 0)
        {
            if (!_missing.TryGetValue(key, out var parts))
                _missing[key] = parts = new();
            parts[(part, color)] = missing;
        }
        else if (_missing.TryGetValue(key, out var parts))
        {
            parts.Remove((part, color));
            if (parts.Count == 0)
                _missing.Remove(key);
        }

        Changed?.Invoke();
    }

    // ---------------------------------------------------------------- searching

    // One page of the user's collection with catalog details, most recently changed first.
    // onlyMissing limits it to items missing at least one piece.
    public async Task<(List<OwnedItem> Rows, int Total)> SearchAsync(string? type, string? search, int skip, int take, bool onlyMissing = false)
    {
        var token = await _auth.GetAccessTokenAsync() ?? throw new AppException("Please sign in again.");
        var path = "rest/v1/my_collection?select=*" +
            (type is null ? "" : $"&item_type=eq.{Uri.EscapeDataString(type)}") +
            (onlyMissing ? "&missing_pieces=gt.0" : "") +
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
            _missing = new();
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

        // Separately, so the collection still loads before the missing-pieces table exists (migration 006)
        try
        {
            var token = await _auth.GetAccessTokenAsync();
            var rows = new List<MissingRow>();
            while (token is not null)
            {
                var page = await _rest.GetAsync<MissingRow>($"rest/v1/collection_missing_parts?select=item_type,item_number,part_num,color_id,missing&order=id&limit={PageSize}&offset={rows.Count}", token);
                rows.AddRange(page);
                if (page.Count < PageSize)
                    break;
            }

            _missing = rows
                .GroupBy(r => CatalogItem.MakeKey(r.ItemType, r.ItemNumber))
                .ToDictionary(g => g.Key, g => g.ToDictionary(r => (r.PartNumber, r.ColorId), r => r.Missing));
        }
        catch (Exception ex)
        {
            _missing = new();
            Console.WriteLine("Loading missing pieces failed: " + ex.Message);
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

    private class MissingRow
    {
        [JsonPropertyName("item_type")] public string ItemType { get; set; } = "";
        [JsonPropertyName("item_number")] public string ItemNumber { get; set; } = "";
        [JsonPropertyName("part_num")] public string PartNumber { get; set; } = "";
        [JsonPropertyName("color_id")] public int ColorId { get; set; }
        [JsonPropertyName("missing")] public int Missing { get; set; }
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

    [JsonPropertyName("bricklink_number")]
    public string? BrickLinkNumber { get; set; }

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("num_parts")]
    public int? NumParts { get; set; }
    // The same item as a catalog item, for the shared item card
    public CatalogItem ToCatalogItem() => new()
    {
        Type = Type,
        Number = Number,
        Name = Name ?? Number,
        CategoryName = CategoryName,
        YearReleased = YearReleased,
        BrickLinkNumber = BrickLinkNumber,
        SourceImageUrl = ImageUrl,
        NumParts = NumParts
    };
}
