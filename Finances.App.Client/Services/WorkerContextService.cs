using Finances.App.Client.Services.Storage;

namespace Finances.App.Client.Services;

public class WorkerContextService
{
    private const string StorageKey = "selectedWorkerId";

    private readonly IKeyValueStorage _storage;
    private int? _selectedWorkerId;

    public event Action? OnChange;

    public WorkerContextService(IKeyValueStorage storage)
    {
        _storage = storage;
    }

    public int? SelectedWorkerId => _selectedWorkerId;

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _storage.GetItemAsync(StorageKey);
            if (int.TryParse(stored, out var id))
            {
                _selectedWorkerId = id;
            }
        }
        catch
        {
            // Storage unavailable (e.g. prerender) — start with no selection.
        }
    }

    public async Task SetWorkerAsync(int? workerId)
    {
        _selectedWorkerId = workerId;
        try
        {
            if (workerId.HasValue)
                await _storage.SetItemAsync(StorageKey, workerId.Value.ToString());
            else
                await _storage.RemoveItemAsync(StorageKey);
        }
        catch
        {
            // Storage unavailable — keep the in-memory selection for this session.
        }
        OnChange?.Invoke();
    }

    public async Task EnsureWorkerExistsAsync(IEnumerable<int> validWorkerIds)
    {
        await InitializeAsync();

        if (_selectedWorkerId.HasValue && !validWorkerIds.Contains(_selectedWorkerId.Value))
        {
            await SetWorkerAsync(null);
        }
    }
}
