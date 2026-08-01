using System.Globalization;

namespace Finances.App.Client.Services;

public static class DateKey
{
    /// <summary>
    /// Formats a date as an invariant ISO yyyy-MM-dd key. Plain string
    /// interpolation ($"{d:yyyy-MM-dd}") uses the browser culture's calendar,
    /// which produces Buddhist/Hijri years on some locales and then fails the
    /// invariant parse on the other side — silently dropping date filters.
    /// </summary>
    public static string ToDateKey(this DateTime date)
    {
        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
