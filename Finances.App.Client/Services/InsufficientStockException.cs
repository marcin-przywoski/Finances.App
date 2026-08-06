namespace Finances.App.Client.Services;

/// <summary>
/// Thrown when a sale (or a sale edit / undo) asks for more units than are in
/// stock. Carries the raw numbers so the UI layer can present a localized
/// message; the base Message stays English for logs.
/// </summary>
public sealed class InsufficientStockException : Exception
{
    public InsufficientStockException(string productName, int available, int requested)
        : base($"Only {available} of \"{productName}\" in stock — cannot sell {requested}.")
    {
        ProductName = productName;
        Available = available;
        Requested = requested;
    }

    public string ProductName { get; }
    public int Available { get; }
    public int Requested { get; }
}
