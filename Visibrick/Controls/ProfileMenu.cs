using Microsoft.Maui.Controls.Shapes;
using Visibrick.Services.Auth;

namespace Visibrick.Controls;

// The menu that drops down from the profile picture in the top-right corner.
// Put one as the last child of a page's root Grid (so it covers the page), and point a ProfileButton at it.
// Clicking outside the card, or choosing an option, closes it.
public class ProfileMenu : ContentView
{
    private readonly AuthService _auth;
    private readonly ProfileService _profile;
    private readonly VerticalStackLayout _items = new() { Spacing = 2 };
    private readonly Border _card;

    public ProfileMenu()
    {
        var services = IPlatformApplication.Current!.Services;
        _auth = services.GetRequiredService<AuthService>();
        _profile = services.GetRequiredService<ProfileService>();

        IsVisible = false;

        // Covers the whole page so a click anywhere outside the card closes the menu.
        // (Needs a nearly transparent background: fully transparent areas don't receive clicks on Windows.)
        var dismissLayer = new Grid { BackgroundColor = Color.FromRgba(0, 0, 0, 0.01) };
        dismissLayer.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(Hide) });

        _card = new Border
        {
            Style = (Style)Application.Current!.Resources["ClayCard"],
            StrokeShape = new RoundRectangle { CornerRadius = 24 },
            Padding = new Thickness(8),
            WidthRequest = 280,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            Margin = new Thickness(0, 76, 24, 0),
            Shadow = new Shadow { Brush = (Brush)new SolidColorBrush((Color)Application.Current.Resources["ClayShadow"]), Offset = new Point(0, 12), Radius = 28, Opacity = 0.3f },
            Content = _items
        };

        Content = new Grid { Children = { dismissLayer, _card } };
    }

    public void Toggle()
    {
        if (IsVisible)
            Hide();
        else
            Show();
    }

    public async void Show()
    {
        Build();
        _card.Opacity = 0;
        _card.Scale = 0.94;
        _card.AnchorX = 1;
        _card.AnchorY = 0;
        IsVisible = true;
        await Task.WhenAll(_card.FadeTo(1, 120, Easing.CubicOut), _card.ScaleTo(1, 160, Easing.CubicOut));
    }

    public async void Hide()
    {
        if (!IsVisible)
            return;

        await _card.FadeTo(0, 90, Easing.CubicIn);
        IsVisible = false;
    }

    // Rebuilt each time it opens, so it always matches who's signed in
    private void Build()
    {
        _items.Children.Clear();

        if (_auth.IsSignedIn)
        {
            // Show the email under the name, unless the "name" already is the email (no name set yet)
            var name = _profile.DisplayName ?? "";
            var email = _auth.CurrentUser?.Email;
            _items.Children.Add(Header(name, email == name ? null : email));
            _items.Children.Add(Divider());
            _items.Children.Add(Item("Profile settings", () => Shell.Current.GoToAsync("profile")));
            _items.Children.Add(Item("My collection", () => Shell.Current.GoToAsync("//collection")));
            _items.Children.Add(Divider());
            _items.Children.Add(Item("Sign out", _auth.SignOutAsync, isDestructive: true));
        }
        else
        {
            _items.Children.Add(Header("Not signed in", "Sign in to save your collection"));
            _items.Children.Add(Divider());
            _items.Children.Add(Item("Sign in or create account", () => Shell.Current.GoToAsync("//collection")));
        }
    }

    private View Header(string title, string? subtitle)
    {
        var text = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label { Text = title, Style = (Style)Application.Current!.Resources["ClayCardTitle"], LineBreakMode = LineBreakMode.TailTruncation },
                new Label { Text = subtitle, Style = (Style)Application.Current.Resources["ClayCaption"], LineBreakMode = LineBreakMode.TailTruncation, IsVisible = subtitle is not null }
            }
        };

        var avatar = new AvatarView
        {
            Size = 44,
            ImageUrl = _auth.IsSignedIn ? _profile.AvatarUrl : null,
            Initials = _auth.IsSignedIn ? _profile.Initials : null
        };

        var grid = new Grid { ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Star)], ColumnSpacing = 12, Padding = new Thickness(10, 8) };
        grid.Add(avatar, 0);
        grid.Add(text, 1);
        return grid;
    }

    private View Item(string text, Func<Task> action, bool isDestructive = false)
    {
        var label = new Label
        {
            Text = text,
            Style = (Style)Application.Current!.Resources[isDestructive ? "ClayErrorLabel" : "ClayCardTitle"],
            Padding = new Thickness(12, 12),
            VerticalOptions = LayoutOptions.Center
        };

        var row = new Grid { Children = { label } };
        PressFeedback.SetIsEnabled(row, true);
        SemanticProperties.SetDescription(row, text);
        row.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                Hide();
                await action();
            })
        });

        return row;
    }

    private static View Divider() => new BoxView
    {
        HeightRequest = 1,
        Margin = new Thickness(8, 4),
        Color = Color.FromArgb("#228B6CF5"),
        BackgroundColor = Colors.Transparent
    };
}
