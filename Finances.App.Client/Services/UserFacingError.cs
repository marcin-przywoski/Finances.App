using System.Globalization;
using System.Resources;
using System.Text;
using Finances.App.Client.Services.Storage;

namespace Finances.App.Client.Services;

/// <summary>
/// Maps store exceptions to messages safe to show in a toast. The hot-path
/// errors are localized here through the AppStrings resources (a static
/// ResourceManager, because this helper runs outside DI); rare
/// import-validation messages stay English by design.
/// </summary>
public static class UserFacingError
{
    private static readonly ResourceManager Strings =
        new("Finances.App.Client.Resources.AppStrings", typeof(AppStrings).Assembly);

    private static string Localized(string key, string fallback)
    {
        return Strings.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
    }

    public static string Message(Exception ex)
    {
        switch (ex)
        {
            case InsufficientStockException stock:
                return string.Format(
                    CultureInfo.CurrentCulture,
                    CompositeFormat.Parse(Localized("Error_InsufficientStock", "Only {0} of \"{1}\" in stock — cannot sell {2}.")),
                    stock.Available, stock.ProductName, stock.Requested);
            case KeyNotFoundException:
                return Localized("Error_ItemGone", "That item no longer exists — it may have been deleted in another tab.");
            case StorageWriteException:
                return Localized("Error_StorageWrite", ex.Message);
            case ConcurrentUpdateException:
                return Localized("Error_ConcurrentUpdate", ex.Message);
            case InvalidDataException:
                return ex.Message;
            default:
                Console.Error.WriteLine($"Unexpected error: {ex}");
                return Localized("Error_Generic", "Something went wrong. Please try again.");
        }
    }
}
