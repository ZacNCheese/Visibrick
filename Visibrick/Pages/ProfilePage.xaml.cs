using Visibrick.Services.Auth;

namespace Visibrick.Pages;

public partial class ProfilePage : ContentPage
{
	private const int MinPasswordLength = 8;

	private readonly AuthService _auth;
	private readonly ProfileService _profile;

	// Deleting takes two clicks: the first asks for confirmation
	private bool _confirmingDelete;

	public ProfilePage(AuthService auth, ProfileService profile)
	{
		InitializeComponent();
		_auth = auth;
		_profile = profile;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		_profile.Changed += OnProfileChanged;
		_auth.Changed += OnAuthChanged;

		var user = _auth.CurrentUser;
		NameEntry.Text = _profile.DisplayName == user?.Email ? "" : _profile.DisplayName;
		EmailLabel.Text = user?.Email ?? "";
		ProvidersLabel.Text = "Signs in with " + string.Join(" and ", (user?.Providers ?? []).Select(ProviderName));
		PasswordCard.IsVisible = user?.HasPassword == true;
		UpdatePhoto();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		_profile.Changed -= OnProfileChanged;
		_auth.Changed -= OnAuthChanged;
	}

	private void OnProfileChanged(object? sender, EventArgs e) => UpdatePhoto();

	// Signed out (here or elsewhere): there's nothing to show, so go back
	private async void OnAuthChanged(object? sender, EventArgs e)
	{
		if (!_auth.IsSignedIn)
			await Shell.Current.GoToAsync("//collection");
	}

	private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

	private void UpdatePhoto()
	{
		Avatar.ImageUrl = _profile.AvatarUrl;
		Avatar.Initials = _profile.Initials;
		RemovePhotoButton.IsVisible = _profile.AvatarUrl is not null;
	}

	private async void OnChangePhotoClicked(object? sender, EventArgs e)
	{
		FileResult? photo;
		try
		{
			photo = await MediaPicker.Default.PickPhotoAsync(new MediaPickerOptions { Title = "Choose a profile picture" });
		}
		catch (PermissionException)
		{
			ShowMessage(PhotoMessage, "Visibrick needs permission to see your photos. You can allow it in your device settings.");
			return;
		}

		if (photo is null)
			return;

		await RunAsync(PhotoMessage, "Uploading…", "Photo updated.", () => _profile.UploadAvatarAsync(photo));
	}

	private async void OnRemovePhotoClicked(object? sender, EventArgs e)
		=> await RunAsync(PhotoMessage, "Removing…", "Photo removed.", _profile.RemoveAvatarAsync);

	private async void OnSaveNameClicked(object? sender, EventArgs e)
		=> await RunAsync(NameMessage, "Saving…", "Saved.", () => _profile.UpdateDisplayNameAsync(NameEntry.Text ?? ""));

	private void OnShowPasswordClicked(object? sender, EventArgs e)
	{
		NewPasswordEntry.IsPassword = !NewPasswordEntry.IsPassword;
		ShowPasswordButton.Text = NewPasswordEntry.IsPassword ? "Show" : "Hide";
	}

	private async void OnChangePasswordClicked(object? sender, EventArgs e)
	{
		var password = NewPasswordEntry.Text ?? "";
		if (password.Length < MinPasswordLength)
		{
			ShowMessage(PasswordMessage, $"Use at least {MinPasswordLength} characters.");
			return;
		}

		await RunAsync(PasswordMessage, "Updating…", "Password updated.", async () =>
		{
			await _auth.ChangePasswordAsync(password);
			NewPasswordEntry.Text = "";
		});
	}

	private async void OnSignOutClicked(object? sender, EventArgs e) => await _auth.SignOutAsync();

	private async void OnDeleteClicked(object? sender, EventArgs e)
	{
		if (!_confirmingDelete)
		{
			_confirmingDelete = true;
			DeleteMessage.Text = "Are you sure? Your account, profile and collection will be gone for good.";
			DeleteButton.Text = "Yes, permanently delete my account";
			return;
		}

		await RunAsync(DeleteMessage, "Deleting your account…", "", async () =>
		{
			await _profile.DeleteUploadedAvatarAsync();
			await _auth.DeleteAccountAsync();
		});
	}

	// Runs an action, showing progress, success or a friendly error in the given label
	private static async Task RunAsync(Label message, string progress, string done, Func<Task> action)
	{
		ShowMessage(message, progress);
		try
		{
			await action();
			ShowMessage(message, done);
		}
		catch (AuthException ex)
		{
			ShowMessage(message, ex.Message);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("PROFILE ACTION FAILED: " + ex);
			ShowMessage(message, "Something went wrong. Please try again.");
		}
	}

	private static void ShowMessage(Label label, string text)
	{
		label.Text = text;
		label.IsVisible = text.Length > 0;
	}

	private static string ProviderName(string provider) => provider switch
	{
		"email" => "email and password",
		"google" => "Google",
		"apple" => "Apple",
		_ => provider
	};
}
