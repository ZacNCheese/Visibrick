using System.ComponentModel;
using System.Runtime.CompilerServices;
using Visibrick.Core.Models;

namespace Visibrick.Pages;

// One category card on the Catalog tab
public class CatalogCategory : INotifyPropertyChanged
{
    private string _countText = "";

    public CatalogCategory(CatalogType type, string icon)
    {
        Type = type;
        Icon = icon;
    }

    public CatalogType Type { get; }

    public string Name => Type.Name;

    // Image file for the card, e.g. "icon_sets.png"
    public string Icon { get; }

    // e.g. "21,898 sets"
    public string CountText
    {
        get => _countText;
        set
        {
            if (_countText == value)
                return;

            _countText = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
