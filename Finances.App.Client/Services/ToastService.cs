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
}

public class ToastService
{
    public event Action? OnChange;
    public List<ToastMessage> Toasts { get; } = new();

    public void Show(string message, ToastLevel level = ToastLevel.Success)
    {
        var toast = new ToastMessage { Message = message, Level = level };
        Toasts.Add(toast);
        OnChange?.Invoke();

        _ = RemoveAfterDelay(toast);
    }

    private async Task RemoveAfterDelay(ToastMessage toast)
    {
        // Fire-and-forget: guard so a subscriber throwing never becomes an
        // unobserved task exception.
        try
        {
            await Task.Delay(2700);
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
