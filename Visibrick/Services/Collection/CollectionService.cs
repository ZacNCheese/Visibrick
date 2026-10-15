using System.Diagnostics;
using System.Text.Json.Serialization;
using Visibrick.Core.Models;
using Visibrick.Data;
using Visibrick.Models;
using Visibrick.Services.Auth;

namespace Visibrick.Services.Collection;

// The signed-in user's collection. The server (collection_items) is the source of truth; a copy is kept on the
// device so the collection shows instantly and works offline. Changes show immediately and are then saved to
// the server; if saving fails, the change is undone and the error is passed on.
public class CollectionService
{
    private const string Table = "collection_items";
    private const int PageSize = 1000;

    // Which user the device's cached collection belongs to
    private const string CachedUserKey = "collection.user";

    private readonly AuthService _auth;
    private readonly UserDataClient _api;
    private readonly CollectionRepository _repo;

    // Type:number → quantity, for quick "do I own this?" checks on every card
    private Dictionary<string, int> _quantities = new();
    private string? _loadedUserId;

    // One change at a time, so quick repeated taps are saved to the server in order
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public CollectionService(AuthService auth, UserDataClient api, CollectionRepository repo)
    {
        _auth = auth;
        _api = api;
        _repo = repo;

        _auth.Changed += async (_, _) => await OnAuthChangedAsync();

        // Created on first use, possibly after the user was already signed in: load for them now
        _ = OnAuthChangedAsync();
    }

    // The whole collection was loaded or cleared. Raised on the main thread.
    public event EventHandler? Changed;

    // One item's quantity changed (0 = removed). Raised on the main thread.
    public event EventHandler<ItemQuantityChangedEventArgs>? ItemChanged;

    public int Count => _quantities.Count;

    public int TotalQuantity => _quantities.Values.Sum();

    public int QuantityOf(string type, string number)
        => _quantities.GetValueOrDefault(CatalogItem.MakeKey(type, number));

    public Task AddOneAsync(string type, string number) => SetQuantityAsync(type, number, QuantityOf(type, number) + 1);

    // Sets how many the user owns; 0 removes it from the collection
    public async Task SetQuantityAsync(string type, string number, int quantity)
    {
        if (!_auth.IsSignedIn)
            throw new AuthException("Sign in to start your collection.");

        await _writeGate.WaitAsync();
        try
        {
            var previous = QuantityOf(type, number);
            if (previous == quantity)
                return;

            await ApplyAsync(type, number, quantity);
            try
            {
                if (quantity > 0)
                {
                    await _api.UpsertAsync(Table, new CollectionRow
                    {
                        UserId = _auth.CurrentUser!.Id,
                        ItemType = type,
                        ItemNumber = number,
                        Quantity = quantity,
                        UpdatedAt = DateTimeOffset.UtcNow
                    }, onConflict: "user_id,item_type,item_number");
                }
                else
                {
                    await _api.DeleteAsync($"{Table}?item_type=eq.{Uri.EscapeDataString(type)}&item_number=eq.{Uri.EscapeDataString(number)}");
                }
            }
            catch
            {
                // Saving failed: put it back how it was so the screen matches the server
                await ApplyAsync(type, number, previous);
                throw;
            }
        }
        finally
        {
            _writeGate.Release();
        }
    }

    // Reloads the collection from the server (e.g. to pick up changes made on another device)
    public async Task RefreshAsync()
    {
        var user = _auth.CurrentUser;
        if (user is null)
            return;

        try
        {
            var rows = new List<CollectionRow>();
            while (true)
            {
                var page = await _api.GetAsync<CollectionRow>(
                    $"{Table}?select=item_type,item_number,quantity,updated_at&order=id&limit={PageSize}&offset={rows.Count}");
                rows.AddRange(page);
                if (page.Count < PageSize)
                    break;
            }

            var entries = rows.Select(r => new CollectionEntry
            {
                Type = r.ItemType,
                Number = r.ItemNumber,
                Quantity = r.Quantity,
                UpdatedAt = r.UpdatedAt.UtcDateTime
            }).ToList();

            await _repo.ReplaceAllAsync(entries);
            Preferences.Set(CachedUserKey, user.Id);
            SetQuantities(entries);
        }
        catch (Exception ex)
        {
            // Offline, or the table isn't set up yet: keep showing the cached copy
            Debug.WriteLine("COLLECTION REFRESH FAILED: " + ex.Message);
        }
    }

    private async Task OnAuthChangedAsync()
    {
        // Token refreshes also raise Changed; only react when the signed-in user actually changes
        var userId = _auth.CurrentUser?.Id;
        if (userId == _loadedUserId)
            return;

        _loadedUserId = userId;

        if (userId is null)
        {
            // Signed out: don't leave their collection on the device
            await _repo.ClearAsync();
            Preferences.Remove(CachedUserKey);
            SetQuantities([]);
            return;
        }

        // Show the cached copy straight away if it's this user's, then update it from the server
        if (Preferences.Get(CachedUserKey, null) == userId)
            SetQuantities(await _repo.GetAllAsync());
        else
            await _repo.ClearAsync();

        await RefreshAsync();
    }

    private async Task ApplyAsync(string type, string number, int quantity)
    {
        var key = CatalogItem.MakeKey(type, number);

        if (quantity > 0)
        {
            _quantities[key] = quantity;
            await _repo.SetAsync(new CollectionEntry { Type = type, Number = number, Quantity = quantity, UpdatedAt = DateTime.UtcNow });
        }
        else
        {
            _quantities.Remove(key);
            await _repo.RemoveAsync(key);
        }

        MainThread.BeginInvokeOnMainThread(() => ItemChanged?.Invoke(this, new ItemQuantityChangedEventArgs(key, quantity)));
    }

    private void SetQuantities(IEnumerable<CollectionEntry> entries)
    {
        _quantities = entries.ToDictionary(e => e.Key, e => e.Quantity);
        MainThread.BeginInvokeOnMainThread(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    // A row of the server's collection_items table
    private class CollectionRow
    {
        [JsonPropertyName("user_id")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? UserId { get; set; }

        [JsonPropertyName("item_type")]
        public string ItemType { get; set; } = "";

        [JsonPropertyName("item_number")]
        public string ItemNumber { get; set; } = "";

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }
    }
}

public class ItemQuantityChangedEventArgs(string key, int quantity) : EventArgs
{
    public string Key { get; } = key;

    public int Quantity { get; } = quantity;
}
