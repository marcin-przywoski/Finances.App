namespace Finances.App.Shared;

/// <summary>
/// User preferences stored inside the snapshot so they travel with backups.
/// Every property must be optional with a normalization-time default: adding
/// one is an additive change and does not require a schema version bump.
/// </summary>
public class AppSettings
{
    /// <summary>
    /// ISO 4217 code from the app's built-in currency list;
    /// null means "use the browser culture's currency format".
    /// </summary>
    public string? CurrencyCode { get; set; }
}
