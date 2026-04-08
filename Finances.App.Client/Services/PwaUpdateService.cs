using System.Globalization;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Finances.App.Client.Services;

public sealed record PwaReleaseInfo(
    string? ReleaseId,
    string? Title,
    string? Summary,
    DateTimeOffset? PublishedUtc,
    IReadOnlyList<string> Changes);

public sealed record PwaUpdateState(
    bool InstallAvailable,
    bool UpdateAvailable,
    bool IsStandalone,
    bool IsOfflineReady,
    bool IsCheckingForUpdates,
    bool JustUpdated,
    string? CurrentVersion,
    string? AvailableVersion,
    PwaReleaseInfo? InstalledRelease,
    PwaReleaseInfo? AvailableRelease,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? LastAppliedUpdateUtc)
{
    public static PwaUpdateState Empty { get; } = new(false, false, false, false, false, false, null, null, null, null, null, null);
}

public sealed class PwaUpdateService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IJSRuntime _js;
    private readonly ToastService _toast;
    private DotNetObjectReference<PwaUpdateService>? _dotNetRef;
    private bool _initialized;
    private bool _disposed;

    public event Action? OnChange;

    public PwaUpdateState State { get; private set; } = PwaUpdateState.Empty;

    public PwaUpdateService(IJSRuntime js, ToastService toast)
    {
        _js = js;
        _toast = toast;
    }

    public async Task EnsureInitializedAsync()
    {
        if (_initialized || _disposed)
        {
            return;
        }

        _initialized = true;
        _dotNetRef = DotNetObjectReference.Create(this);
        await _js.InvokeVoidAsync("financePwa.register", _dotNetRef);
    }

    public async Task<bool> CheckForUpdatesAsync()
    {
        await EnsureInitializedAsync();
        return await _js.InvokeAsync<bool>("financePwa.checkForUpdates");
    }

    public async Task<bool> ApplyUpdateAsync()
    {
        await EnsureInitializedAsync();
        return await _js.InvokeAsync<bool>("financePwa.applyUpdate");
    }

    public async Task<bool> PromptInstallAsync()
    {
        await EnsureInitializedAsync();
        return await _js.InvokeAsync<bool>("financePwa.promptInstall");
    }

    [JSInvokable]
    public Task UpdateState(string json)
    {
        var nextState = JsonSerializer.Deserialize<PwaUpdateStateDto>(json, JsonOptions)?.ToState() ?? PwaUpdateState.Empty;
        var previousVersion = State.CurrentVersion;
        var shouldToast = nextState.JustUpdated && (!State.JustUpdated || !string.Equals(previousVersion, nextState.CurrentVersion, StringComparison.Ordinal));

        State = nextState;

        if (shouldToast)
        {
            var message = string.IsNullOrWhiteSpace(nextState.CurrentVersion)
                ? "The installed app was updated."
                : $"The installed app was updated to build {nextState.CurrentVersion}.";

            _toast.Show(message);
        }

        OnChange?.Invoke();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (_initialized)
            {
                await _js.InvokeVoidAsync("financePwa.dispose");
            }
        }
        catch (JSDisconnectedException)
        {
            return;
        }

        _dotNetRef?.Dispose();
    }

    private static DateTimeOffset? ParseDateTimeOffset(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    private sealed class PwaUpdateStateDto
    {
        public bool InstallAvailable { get; set; }
        public bool UpdateAvailable { get; set; }
        public bool IsStandalone { get; set; }
        public bool IsOfflineReady { get; set; }
        public bool IsCheckingForUpdates { get; set; }
        public bool JustUpdated { get; set; }
        public string? CurrentVersion { get; set; }
        public string? AvailableVersion { get; set; }
        public PwaReleaseInfoDto? InstalledRelease { get; set; }
        public PwaReleaseInfoDto? AvailableRelease { get; set; }
        public string? LastCheckedUtc { get; set; }
        public string? LastAppliedUpdateUtc { get; set; }

        public PwaUpdateState ToState()
        {
            return new PwaUpdateState(
                InstallAvailable,
                UpdateAvailable,
                IsStandalone,
                IsOfflineReady,
                IsCheckingForUpdates,
                JustUpdated,
                string.IsNullOrWhiteSpace(CurrentVersion) ? null : CurrentVersion,
                string.IsNullOrWhiteSpace(AvailableVersion) ? null : AvailableVersion,
                InstalledRelease?.ToReleaseInfo(),
                AvailableRelease?.ToReleaseInfo(),
                PwaUpdateService.ParseDateTimeOffset(LastCheckedUtc),
                PwaUpdateService.ParseDateTimeOffset(LastAppliedUpdateUtc));
        }
    }

    private sealed class PwaReleaseInfoDto
    {
        public string? ReleaseId { get; set; }
        public string? Title { get; set; }
        public string? Summary { get; set; }
        public string? PublishedUtc { get; set; }
        public List<string>? Changes { get; set; }

        public PwaReleaseInfo? ToReleaseInfo()
        {
            var normalizedReleaseId = Normalize(ReleaseId);
            var normalizedTitle = Normalize(Title);
            var normalizedSummary = Normalize(Summary);
            var normalizedChanges = (Changes ?? [])
                .Select(Normalize)
                .Where(value => value is not null)
                .Cast<string>()
                .ToArray();
            var publishedUtc = PwaUpdateService.ParseDateTimeOffset(PublishedUtc);

            if (normalizedReleaseId is null
                && normalizedTitle is null
                && normalizedSummary is null
                && publishedUtc is null
                && normalizedChanges.Length == 0)
            {
                return null;
            }

            return new PwaReleaseInfo(normalizedReleaseId, normalizedTitle, normalizedSummary, publishedUtc, normalizedChanges);
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}