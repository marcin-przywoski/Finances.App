using System.Globalization;
using System.Resources;

namespace Finances.App.Shared;

/// <summary>
/// Accessor for the validation-message resources. Written by hand (instead of
/// a generated Designer) because the resx code generator is a Visual Studio
/// custom tool that does not run under `dotnet build`; DataAnnotations only
/// needs public static string properties matching ErrorMessageResourceName.
/// </summary>
public static class ValidationStrings
{
    private static readonly ResourceManager Resources =
        new("Finances.App.Shared.ValidationStrings", typeof(ValidationStrings).Assembly);

    private static string Get(string name, string fallback)
    {
        return Resources.GetString(name, CultureInfo.CurrentUICulture) ?? fallback;
    }

    public static string Required => Get(nameof(Required), "This field is required.");

    public static string Percentage => Get(nameof(Percentage), "Enter a percentage between 0 and 100.");

    public static string Amount => Get(nameof(Amount), "Enter an amount between 0 and 100 000.");

    public static string Quantity => Get(nameof(Quantity), "Enter a quantity between 1 and 10 000.");

    public static string MaxLength => Get(nameof(MaxLength), "Maximum length is {1} characters.");
}
