namespace Finances.App.Client.Services;

public enum NotificationType
{
    Info,
    Warning,
    Action
}

public sealed record AppNotification(
    string Id,
    NotificationType Type,
    string Title,
    string Message,
    DateTime CreatedUtc,
    string? ActionUrl = null,
    string? ActionLabel = null,
    string? Key = null,
    Func<Task>? HandleAction = null,
    string? HandleLabel = null);

public readonly record struct NotificationDraft(
    NotificationType Type,
    string Title,
    string Message,
    string? ActionUrl = null,
    string? ActionLabel = null,
    string? Key = null,
    Func<Task>? HandleAction = null,
    string? HandleLabel = null);

public sealed class NotificationService
{
    private readonly List<AppNotification> _notifications = [];

    public event Action? OnChange;

    public IReadOnlyList<AppNotification> Notifications => _notifications;

    public int UnreadCount => _notifications.Count;

    public void Add(NotificationType type, string title, string message)
    {
        var id = Guid.NewGuid().ToString("N");
        _notifications.Add(new AppNotification(id, type, title, message, DateTime.UtcNow));
        OnChange?.Invoke();
    }

    public void Dismiss(string id)
    {
        var item = _notifications.FirstOrDefault(n => n.Id == id);
        if (item is not null)
        {
            _notifications.Remove(item);
            OnChange?.Invoke();
        }
    }

    public void DismissAll()
    {
        _notifications.Clear();
        OnChange?.Invoke();
    }

    public void ReplaceAll(IEnumerable<(NotificationType Type, string Title, string Message)> items)
    {
        _notifications.Clear();
        foreach (var (type, title, message) in items)
        {
            var id = Guid.NewGuid().ToString("N");
            _notifications.Add(new AppNotification(id, type, title, message, DateTime.UtcNow));
        }
        OnChange?.Invoke();
    }

    /// <summary>
    /// Replace the entire notification list with rich drafts (optionally including action links and handlers).
    /// Stable keys survive snapshot refreshes so the same reminder does not rotate its dismiss id.
    /// </summary>
    public void ReplaceAll(IEnumerable<NotificationDraft> drafts)
    {
        var existingById = _notifications.Where(n => !string.IsNullOrEmpty(n.Key))
            .ToDictionary(n => n.Key!, n => n.Id);

        _notifications.Clear();
        foreach (var draft in drafts)
        {
            var id = draft.Key is not null && existingById.TryGetValue(draft.Key, out var prior)
                ? prior
                : Guid.NewGuid().ToString("N");
            _notifications.Add(new AppNotification(
                id,
                draft.Type,
                draft.Title,
                draft.Message,
                DateTime.UtcNow,
                draft.ActionUrl,
                draft.ActionLabel,
                draft.Key,
                draft.HandleAction,
                draft.HandleLabel));
        }

        OnChange?.Invoke();
    }
}
