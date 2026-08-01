namespace Finances.App.Client.Services.Storage;

/// <summary>
/// Thrown when writing to browser storage fails (typically because the
/// ~5 MB localStorage quota is exhausted). The in-memory state is reverted
/// to the last persisted snapshot before this is thrown.
/// </summary>
public sealed class StorageWriteException : Exception
{
    public StorageWriteException(Exception inner)
        : base("Could not save your data — browser storage may be full. Export a backup now, then remove old records or free up space.", inner)
    {
    }
}

/// <summary>
/// Thrown when a save is refused because another tab wrote newer data
/// since this tab loaded. The view is refreshed with the newer data;
/// the user should retry their change.
/// </summary>
public sealed class ConcurrentUpdateException : Exception
{
    public ConcurrentUpdateException()
        : base("Your data was changed in another tab. The view has been refreshed — please retry your change.")
    {
    }
}
