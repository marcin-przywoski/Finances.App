using Microsoft.JSInterop;

namespace KubiczPlace.Finances.Client.Services;

public class WorkerContextService
{
    private readonly IJSRuntime _js;
    private int? _selectedWorkerId;

    public event Action? OnChange;

    public WorkerContextService(IJSRuntime js)
    {
        _js = js;
    }

    public int? SelectedWorkerId => _selectedWorkerId;

    public async Task InitializeAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("localStorage.getItem", "selectedWorkerId");
            if (int.TryParse(stored, out var id))
            {
                _selectedWorkerId = id;
            }
        }
        catch
        {
            // SSR or prerender — ignore
        }
    }

    public async Task SetWorkerAsync(int? workerId)
    {
        _selectedWorkerId = workerId;
        try
        {
            if (workerId.HasValue)
                await _js.InvokeVoidAsync("localStorage.setItem", "selectedWorkerId", workerId.Value.ToString());
            else
                await _js.InvokeVoidAsync("localStorage.removeItem", "selectedWorkerId");
        }
        catch
        {
            // SSR or prerender — ignore
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
