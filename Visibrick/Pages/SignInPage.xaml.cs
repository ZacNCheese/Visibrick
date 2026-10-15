using System.Net.Mail;
using Visibrick.Services.Auth;

namespace Visibrick.Pages;

public partial class SignInPage : ContentPage
{
	// Matches the minimum password length to set in Supabase (Authentication → Policies / Providers → Email)
	private const int MinPasswordLength = 8;

	private readonly AuthService _auth;
	private bool _creatingAccount;

	public SignInPage(AuthService auth)
	{
		InitializeComponent();
		_auth = auth;
	}

	private async void OnBackClicked(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

	private void OnEmailCompleted(object? sender, EventArgs e) => PasswordEntry.Focus();

	private void OnPasswordCompleted(object? sender, EventArgs e) => OnPrimaryClicked(sender, e);

	private void OnShowPasswordClicked(object? sender, EventArgs e)
	{
		PasswordEntry.IsPassword = !PasswordEntry.IsPassword;
		ShowPasswordButton.Text = PasswordEntry.IsPassword ? "Show" : "Hide";
	}

	private void OnSwitchModeClicked(object? sender, EventArgs e)
	{
		_creatingAccount = !_creatingAccount;
		ShowMessages(null, null);

		TitleLabel.Text = _creatingAccount ? "Create account" : "Sign in";
		SubtitleLabel.Text = _creatingAccount ? "Start tracking your LEGO collection." : "Welcome back. Sign in with your email.";
		PrimaryButton.Text = _creatingAccount ? "Create account" : "Sign in";
		SwitchModeButton.Text = _creatingAccount ? "Already have an account? Sign in" : "New to Visibrick? Create an account";
		PasswordHint.IsVisible = _creatingAccount;
		ForgotButton.IsVisible = !_creatingAccount;
	}

	private async void OnPrimaryClicked(object? sender, EventArgs e)
	{
		var email = EmailEntry.Text?.Trim() ?? "";
		var password = PasswordEntry.Text ?? "";

		if (!IsEmail(email))
		{
			ShowMessages("Enter a valid email address.", null);
			return;
		}

		if (_creatingAccount && password.Length < MinPasswordLength)
		{
			ShowMessages($"Use at least {MinPasswordLength} characters for your password.", null);
			return;
		}

		if (password.Length == 0)
		{
			ShowMessages("Enter your password.", null);
			return;
		}

		await RunBusyAsync(async () =>
		{
			if (!_creatingAccount)
			{
				await _auth.SignInWithEmailAsync(email, password);
				await Shell.Current.GoToAsync("..");
				return;
			}

			if (await _auth.SignUpWithEmailAsync(email, password) == SignUpOutcome.SignedIn)
			{
				await Shell.Current.GoToAsync("..");
				return;
			}

			// Email confirmation is on: switch to sign-in so they can sign in after clicking the link
			OnSwitchModeClicked(null, EventArgs.Empty);
			PasswordEntry.Text = "";
			ShowMessages(null, $"Almost done! We sent a confirmation link to {email}. Click it, then sign in here.");
		});
	}

	private async void OnForgotClicked(object? sender, EventArgs e)
	{
		var email = EmailEntry.Text?.Trim() ?? "";
		if (!IsEmail(email))
		{
			ShowMessages("Enter your email address above first, then tap \"Forgot your password?\" again.", null);
			return;
		}

		await RunBusyAsync(async () =>
		{
			await _auth.SendPasswordResetAsync(email);

			// Same message whether or not the account exists, so this can't reveal who has signed up
			ShowMessages(null, $"If there's an account for {email}, we've emailed a link to reset the password.");
		});
	}

	private async Task RunBusyAsync(Func<Task> action)
	{
		ShowMessages(null, null);
		PrimaryButton.IsEnabled = ForgotButton.IsEnabled = SwitchModeButton.IsEnabled = false;
		BusyIndicator.IsVisible = BusyIndicator.IsRunning = true;
		PrimaryButton.Text = "";

		try
		{
			await action();
		}
		catch (AuthException ex)
		{
			ShowMessages(ex.Message, null);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine("EMAIL SIGN-IN FAILED: " + ex);
			ShowMessages("Something went wrong. Please try again.", null);
		}
		finally
		{
			PrimaryButton.IsEnabled = ForgotButton.IsEnabled = SwitchModeButton.IsEnabled = true;
			BusyIndicator.IsVisible = BusyIndicator.IsRunning = false;
			PrimaryButton.Text = _creatingAccount ? "Create account" : "Sign in";
		}
	}

	private void ShowMessages(string? error, string? info)
	{
		ErrorLabel.Text = error;
		ErrorLabel.IsVisible = error is not null;
		InfoLabel.Text = info;
		InfoLabel.IsVisible = info is not null;
	}

	private static bool IsEmail(string text)
		=> text.Contains('@') && MailAddress.TryCreate(text, out var address) && address.Address == text;
}
