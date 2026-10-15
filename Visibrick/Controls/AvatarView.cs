using Microsoft.Maui.Controls.Shapes;

namespace Visibrick.Controls;

// A round profile picture. Shows the image if there is one, otherwise the person's initials,
// otherwise (nobody signed in) a generic person icon.
public class AvatarView : ContentView
{
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(AvatarView), 40.0,
        propertyChanged: (b, _, _) => ((AvatarView)b).UpdateLayout());

    public static readonly BindableProperty ImageUrlProperty = BindableProperty.Create(
        nameof(ImageUrl), typeof(string), typeof(AvatarView), null,
        propertyChanged: (b, _, _) => ((AvatarView)b).UpdateContent());

    public static readonly BindableProperty InitialsProperty = BindableProperty.Create(
        nameof(Initials), typeof(string), typeof(AvatarView), null,
        propertyChanged: (b, _, _) => ((AvatarView)b).UpdateContent());

    private readonly Border _circle;
    private readonly Label _initials;
    private readonly Image _image;
    private readonly Image _personIcon;

    public AvatarView()
    {
        _initials = new Label
        {
            TextColor = Colors.White,
            FontFamily = "OpenSansSemibold",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        _personIcon = new Image { Source = "icon_person.png", HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        _image = new Image { Aspect = Aspect.AspectFill };

        _circle = new Border
        {
            StrokeThickness = 2,
            Stroke = Color.FromArgb("#40FFFFFF"),
            Padding = 0,
            Background = Application.Current?.Resources.TryGetValue("ClayButtonBrush", out var brush) == true
                ? (Brush)brush
                : new SolidColorBrush(Color.FromArgb("#512BD4")),
            Content = new Grid { Children = { _personIcon, _initials, _image } }
        };

        Content = _circle;
        UpdateLayout();
        UpdateContent();
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public string? ImageUrl
    {
        get => (string?)GetValue(ImageUrlProperty);
        set => SetValue(ImageUrlProperty, value);
    }

    public string? Initials
    {
        get => (string?)GetValue(InitialsProperty);
        set => SetValue(InitialsProperty, value);
    }

    private void UpdateLayout()
    {
        WidthRequest = HeightRequest = Size;
        _circle.StrokeShape = new RoundRectangle { CornerRadius = Size / 2 };
        _initials.FontSize = Size * 0.38;
        _personIcon.WidthRequest = _personIcon.HeightRequest = Size * 0.6;
    }

    private void UpdateContent()
    {
        var hasImage = !string.IsNullOrEmpty(ImageUrl);
        var hasInitials = !string.IsNullOrEmpty(Initials);

        _image.Source = hasImage ? ImageSource.FromUri(new Uri(ImageUrl!)) : null;
        _image.IsVisible = hasImage;
        _initials.Text = Initials;
        _initials.IsVisible = !hasImage && hasInitials;
        _personIcon.IsVisible = !hasImage && !hasInitials;
    }
}
