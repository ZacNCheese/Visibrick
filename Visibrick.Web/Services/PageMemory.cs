using Visibrick.Core.Models;

namespace Visibrick.Web.Services;

// Remembers what a list page had loaded and how far down it was scrolled, keyed by its address, so going Back
// to it puts you where you were instead of reloading the first page at the top.
public class PageMemory
{
    // Enough for normal back-and-forth; the oldest are forgotten first
    private const int MaxPages = 20;

    private readonly Dictionary<string, Snapshot> _pages = new();
    private readonly LinkedList<string> _order = new();

    public Snapshot? Get(string key) => _pages.GetValueOrDefault(key);

    public void Save(string key, Snapshot snapshot)
    {
        if (_pages.ContainsKey(key))
            _order.Remove(key);

        _pages[key] = snapshot;
        _order.AddLast(key);

        while (_order.Count > MaxPages)
        {
            _pages.Remove(_order.First!.Value);
            _order.RemoveFirst();
        }
    }

    // Items: the loaded rows (a copy). Version: CollectionService.Version when saved, for My Collection.
    // Filter, Only and Search: My Collection's filters, which aren't in its address.
    public record Snapshot(
        List<CatalogItem> Items,
        int Total,
        double ScrollY,
        int Version = 0,
        string? Filter = null,
        CollectionFilter Only = CollectionFilter.None,
        string? Search = null);
}
