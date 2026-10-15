using Microsoft.AspNetCore.Components;

namespace Visibrick.Web.Components;

// Small inline icons (they take the current text color, so they follow light/dark mode and nav highlighting)
public static class Icons
{
    // A 2x2 brick: My Collection
    public static readonly MarkupString Collection = new(
        """<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 5.5a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1V8h4V5.5a1 1 0 0 1 1-1h3a1 1 0 0 1 1 1V8h1.5A1.5 1.5 0 0 1 22 9.5v10a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 19.5v-10A1.5 1.5 0 0 1 3.5 8H5z"/></svg>""");

    // Four tiles: Catalog
    public static readonly MarkupString Catalog = new(
        """<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 3h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM15 3h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2zM5 13h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2zM15 13h4a2 2 0 0 1 2 2v4a2 2 0 0 1-2 2h-4a2 2 0 0 1-2-2v-4a2 2 0 0 1 2-2z"/></svg>""");

    // Head and shoulders: the avatar when nobody is signed in
    public static readonly MarkupString Person = new(
        """<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3.5a4.25 4.25 0 1 1 0 8.5 4.25 4.25 0 0 1 0-8.5zM4 19.6c0-3.2 3.6-5.6 8-5.6s8 2.4 8 5.6V21H4z"/></svg>""");
}
