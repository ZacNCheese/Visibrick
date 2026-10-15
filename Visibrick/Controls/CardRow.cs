using System.Collections;

namespace Visibrick.Controls;

// One row of cards in a card grid. The grids are vertical lists of rows (rather than
// CollectionView's GridItemsLayout), because changing GridItemsLayout.Span on Windows
// can crash with "Layout cycle detected". Changing the column count here just regroups the rows.
public class CardRow
{
    public CardRow(IList items, int columns)
    {
        Items = items;
        Columns = columns;
    }

    public IList Items { get; }

    public int Columns { get; }

    public static List<CardRow> Chunk<T>(IEnumerable<T> items, int columns)
        => items.Chunk(columns).Select(chunk => new CardRow(chunk, columns)).ToList();
}
