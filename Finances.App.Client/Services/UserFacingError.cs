using Finances.App.Client.Services.Storage;

namespace Finances.App.Client.Services;

/// <summary>
/// Maps store exceptions to messages safe to show in a toast. Store-thrown
/// InvalidDataException / StorageWriteException / ConcurrentUpdateException
/// messages are written for users; anything else gets a generic message and
/// a console log for diagnosis.
/// </summary>
public static class UserFacingError
{
    public static string Message(Exception ex)
    {
        switch (ex)
        {
            case KeyNotFoundException:
                return "That item no longer exists — it may have been deleted in another tab.";
            case InvalidDataException or StorageWriteException or ConcurrentUpdateException:
                return ex.Message;
            default:
                Console.Error.WriteLine($"Unexpected error: {ex}");
                return "Something went wrong. Please try again.";
        }
    }
}
