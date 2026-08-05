namespace Finances.App.Client.Services;

public enum ToastLevel
{
    Success,
    Error
}

public class ToastMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Message { get; set; } = string.Empty;
    public ToastLevel Level { get; set; }
    public bool FadingOut { get; set; }
    public string? ActionLabel { get; set; }
    public Func<Task>? Action { get; set; }
}

public class ToastService
{
    public event Action? OnChange;
    public List<ToastMessage> Toasts { get; } = new();

    public void Show(string message, ToastLevel level = ToastLevel.Success)
    {
        Show(message, level, actionLabel: null, action: null);
    }

    /// <summary>
    /// Shows a toast with an action button (e.g. "Undo"). Action toasts stay
    /// visible longer so the user has time to react.
    /// </summary>
    public void Show(string message, ToastLevel level, string? actionLabel, Func<Task>? action)
    {
        var toast = new ToastMessage
        {
            Message = message,
            Level = level,
            ActionLabel = action is null ? null : actionLabel,
            Action = action
        };
        Toasts.Add(toast);
        OnChange?.Invoke();

        _ = RemoveAfterDelay(toast, toast.Action is null ? 2700 : 6000);
    }

    /// <summary>
    /// Runs a toast's action once and dismisses it. Errors surface as a new
    /// error toast rather than an unhandled exception in the click handler.
    /// </summary>
    public async Task RunActionAsync(ToastMessage toast)
    {
        var action = toast.Action;
        if (action is null)
        {
            return;
        }

        toast.Action = null;
        Toasts.Remove(toast);
        OnChange?.Invoke();

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Show(UserFacingError.Message(ex), ToastLevel.Error);
        }
    }

    private async Task RemoveAfterDelay(ToastMessage toast, int visibleMs)
    {
        // Fire-and-forget: guard so a subscriber throwing never becomes an
        // unobserved task exception.
        try
        {
            await Task.Delay(visibleMs);
            if (!Toasts.Contains(toast))
            {
                return;
            }

            toast.FadingOut = true;
            OnChange?.Invoke();

            await Task.Delay(300);
            Toasts.Remove(toast);
            OnChange?.Invoke();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Toast cleanup failed: {ex.Message}");
        }
    }
}
