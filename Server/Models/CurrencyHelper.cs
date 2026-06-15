namespace TraderGen.Models;

// Maps user-friendly currency names to SPT template IDs.
public static class CurrencyHelper
{
    // Standard EFT currency template IDs
    public const string Roubles = "5449016a4bdc2d6f028b456f";
    public const string Dollars = "5696686a4bdc2da3298b456a";
    public const string Euros = "569668774bdc2da2298b4568";

    // Convert a currency string ("RUB", "USD", "EUR") to its SPT template ID.
    public static string ToTemplateId(string currency)
    {
        return currency?.ToUpperInvariant() switch
        {
            "RUB" or "ROUBLES" or "RUBLE" => Roubles,
            "USD" or "DOLLARS" or "DOLLAR" => Dollars,
            "EUR" or "EUROS" or "EURO" => Euros,
            _ when currency?.Length == 24 => currency,
            _ => Roubles // Default to roubles
        };
    }

    // Check if a currency string is valid.
    public static bool IsValid(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return false;
        var upper = currency.ToUpperInvariant();
        return upper is "RUB" or "ROUBLES" or "RUBLE"
            or "USD" or "DOLLARS" or "DOLLAR"
            or "EUR" or "EUROS" or "EURO"
            || currency.Length == 24; // Allow raw template IDs
    }
}
