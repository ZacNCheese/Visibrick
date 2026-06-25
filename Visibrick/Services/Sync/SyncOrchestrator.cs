public class SyncOrchestrator
{
    private readonly LegoColorSyncService _colorSync;
    private readonly LegoMinifigSyncService _minifigSync;

    public SyncOrchestrator(
        LegoColorSyncService colorSync,
        LegoMinifigSyncService minifigSync)
    {
        _colorSync = colorSync;
        _minifigSync = minifigSync;
    }

    public async Task FullSyncAsync()
    {
        await _colorSync.SyncLegoColorsAsync();
        await _minifigSync.SyncLegoMinifigsAsync();
    }
}