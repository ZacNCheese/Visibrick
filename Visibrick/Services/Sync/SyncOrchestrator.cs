using System.Diagnostics;
using Visibrick.Data;

namespace Visibrick.Services.Sync;

// Keeps the local catalog up to date automatically. The app calls SyncIfStaleAsync on launch and
// whenever it comes back to the foreground; pages listen to the events to refresh themselves.
public class SyncOrchestrator
{
    // How often to check for new data. New BrickLink files are only imported now and then, so a few hours is plenty.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);
    private const string LastCheckedKey = "catalog:last-checked";

    private readonly CatalogSyncService _catalogSync;
    private readonly CatalogRepository _repo;

    // Only one sync at a time, even if launch and resume fire close together
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SyncOrchestrator(CatalogSyncService catalogSync, CatalogRepository repo)
    {
        _catalogSync = catalogSync;
        _repo = repo;
    }

    // False until the Supabase key is filled in (see SupabaseConfig.cs); nothing is downloaded until then
    public bool IsConfigured => SupabaseCatalogClient.IsConfigured;

    public bool IsSyncing { get; private set; }

    // Goes up by one every time new catalog data is saved. A page remembers the version it loaded,
    // so when it reappears it can tell whether the data changed while it was hidden.
    public int DataVersion { get; private set; }

    // Set when the last attempt failed (e.g. no internet); cleared on the next success
    public bool LastSyncFailed { get; private set; }

    public DateTime? LastChecked
    {
        get
        {
            var value = Preferences.Get(LastCheckedKey, DateTime.MinValue);
            return value == DateTime.MinValue ? null : value;
        }
    }

    // IsSyncing / LastSyncFailed / LastChecked changed. Raised on the main thread.
    public event EventHandler? StateChanged;

    // New catalog data was saved. Raised on the main thread.
    public event EventHandler? DataChanged;

    public async Task SyncIfStaleAsync()
    {
        if (!IsConfigured)
            return;

        var isEmpty = await _repo.CountAllAsync() == 0;
        var checkedRecently = LastChecked is { } last && DateTime.UtcNow - last < CheckInterval;

        if (checkedRecently && !isEmpty && !LastSyncFailed)
            return;

        await SyncAsync();
    }

    public async Task SyncAsync()
    {
        // Already running: don't queue a second sync
        if (!await _gate.WaitAsync(0))
            return;

        try
        {
            IsSyncing = true;
            RaiseOnMainThread(StateChanged);

            // Off the UI thread so downloading and saving never freezes the app
            var changed = await Task.Run(_catalogSync.SyncAsync);

            Preferences.Set(LastCheckedKey, DateTime.UtcNow);
            LastSyncFailed = false;

            if (changed)
            {
                DataVersion++;
                RaiseOnMainThread(DataChanged);
            }
        }
        catch (Exception ex)
        {
            // Not shown as an error dialog: the app keeps working with the data it has and retries next time
            Debug.WriteLine("SYNC FAILED: " + ex);
            LastSyncFailed = true;
        }
        finally
        {
            IsSyncing = false;
            RaiseOnMainThread(StateChanged);
            _gate.Release();
        }
    }

    private void RaiseOnMainThread(EventHandler? handler)
        => MainThread.BeginInvokeOnMainThread(() => handler?.Invoke(this, EventArgs.Empty));
}
