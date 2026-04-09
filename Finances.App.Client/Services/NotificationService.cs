namespace Finances.App.Client.Services;

public enum NotificationType
{
    Info,
    Warning,
    Action
}

public sealed record AppNotification(string Id, NotificationType Type, string Title, string Message, DateTime CreatedUtc);

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
}
