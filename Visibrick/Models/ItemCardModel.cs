using System.ComponentModel;
using System.Runtime.CompilerServices;
using Visibrick.Core.Models;

namespace Visibrick.Models;

// What an item card shows: the catalog item plus how many the user owns. Quantity changes update the card's badge.
public class ItemCardModel : INotifyPropertyChanged
{
    private int _quantity;

    public ItemCardModel(CatalogItem item, int quantity)
    {
        Item = item;
        _quantity = quantity;
    }

    public CatalogItem Item { get; }

    public string Key => Item.Key;

    public int Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity == value)
                return;

            _quantity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOwned));
            OnPropertyChanged(nameof(IsNotOwned));
            OnPropertyChanged(nameof(BadgeText));
        }
    }

    public bool IsOwned => Quantity > 0;

    public bool IsNotOwned => Quantity == 0;

    // ✓ for one, ×3 for several
    public string BadgeText => Quantity > 1 ? $"×{Quantity}" : "✓";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
