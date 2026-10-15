namespace Visibrick.Web.Services;

// Short messages shown at the bottom of the screen for a few seconds (e.g. "Couldn't add it: ...")
public class ToastService
{
    public string? Message { get; private set; }

    public event Action? Changed;

    public async void Show(string message)
    {
        Message = message;
        Changed?.Invoke();

        var shown = message;
        await Task.Delay(TimeSpan.FromSeconds(5));
        if (Message == shown)
        {
            Message = null;
            Changed?.Invoke();
        }
    }
}
