using SQLite;
using Visibrick.Core.Models;

namespace Visibrick.Models;

// One item in the signed-in user's collection, as cached on this device.
// The server's collection_items table is the source of truth; this copy makes the app fast and work offline.
public class CollectionEntry
{
    // Same format as CatalogItem.Key ("S:75192-1"), so the two tables can be joined
    [PrimaryKey]
    public string Key
    {
        get => CatalogItem.MakeKey(Type, Number);
        set { }
    }

    [Indexed]
    public string Type { get; set; } = "";

    public string Number { get; set; } = "";

    public int Quantity { get; set; }

    public DateTime UpdatedAt { get; set; }
}

// A catalog item together with how many the user owns (the result of joining CatalogItem and CollectionEntry)
public class OwnedItem : CatalogItem
{
    public int Quantity { get; set; }
}
