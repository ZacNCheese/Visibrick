using Visibrick.Services.Auth;

namespace Visibrick.Controls;

// The profile picture in the top-right corner of the main pages. Opens its ProfileMenu when clicked.
//
//   <controls:ProfileButton Menu="{x:Reference ProfileMenu}" />
public class ProfileButton : ContentView
{
    public static readonly BindableProperty MenuProperty = BindableProperty.Create(
        nameof(Menu), typeof(ProfileMenu), typeof(ProfileButton));

    private readonly AuthService _auth;
    private readonly ProfileService _profile;
    private readonly AvatarView _avatar = new() { Size = 44 };

    public ProfileButton()
    {
        var services = IPlatformApplication.Current!.Services;
        _auth = services.GetRequiredService<AuthService>();
        _profile = services.GetRequiredService<ProfileService>();

        Content = _avatar;
        VerticalOptions = LayoutOptions.Start;
        PressFeedback.SetIsEnabled(this, true);
        ToolTipProperties.SetText(this, "Account");
        SemanticProperties.SetDescription(this, "Account menu");
        GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => Menu?.Toggle()) });

        // Only listen while on screen, so pages that come and go don't keep the services holding on to them
        Loaded += (_, _) => { _profile.Changed += OnProfileChanged; _auth.Changed += OnProfileChanged; Update(); };
        Unloaded += (_, _) => { _profile.Changed -= OnProfileChanged; _auth.Changed -= OnProfileChanged; };
    }

    public ProfileMenu? Menu
    {
        get => (ProfileMenu?)GetValue(MenuProperty);
        set => SetValue(MenuProperty, value);
    }

    private void OnProfileChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        _avatar.ImageUrl = _auth.IsSignedIn ? _profile.AvatarUrl : null;
        _avatar.Initials = _auth.IsSignedIn ? _profile.Initials : null;
    }
}
