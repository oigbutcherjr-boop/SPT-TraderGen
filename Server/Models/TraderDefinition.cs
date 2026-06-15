using System.Text.Json.Serialization;

namespace TraderGen.Models;

/// <summary>
/// Root model for a trader pack JSON file.
/// This is the schema that non-programmers fill out (or generate via the TraderGen Tool).
/// </summary>
public class TraderDefinition
{
    /// <summary>
    /// Unique trader ID. Should be a 24-character hex string (MongoDB ObjectId format).
    /// The TraderGen Tool can auto-generate this.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Short display name shown in the trader list (e.g. "Saria").
    /// </summary>
    [JsonPropertyName("nickname")]
    public string Nickname { get; set; } = string.Empty;

    /// <summary>
    /// First name used in locale data.
    /// </summary>
    [JsonPropertyName("firstName")]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Last name / surname used in locale data.
    /// </summary>
    [JsonPropertyName("lastName")]
    public string LastName { get; set; } = "Unknown";

    /// <summary>
    /// Full display name. If empty, defaults to "nickname lastName".
    /// </summary>
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    /// <summary>
    /// Location text shown in trader screen (e.g. "Reserve Underground Bunker").
    /// </summary>
    [JsonPropertyName("location")]
    public string Location { get; set; } = "Unknown";

    /// <summary>
    /// Trader description shown when clicking the trader info button.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "A custom trader.";

    /// <summary>
    /// Relative path to the trader avatar image file inside the trader pack folder.
    /// Example: "assets/avatar.jpg"
    /// </summary>
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;

    /// <summary>
    /// Default currency the trader uses: "RUB", "USD", or "EUR".
    /// </summary>
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "RUB";

    /// <summary>
    /// Whether the trader is unlocked from the start (default true).
    /// </summary>
    [JsonPropertyName("unlockedByDefault")]
    public bool UnlockedByDefault { get; set; } = true;

    /// <summary>
    /// Whether the trader can buy items from the player.
    /// </summary>
    [JsonPropertyName("buyerEnabled")]
    public bool BuyerEnabled { get; set; } = true;

    /// <summary>
    /// Whether the trader appears on the flea market / ragfair.
    /// </summary>
    [JsonPropertyName("ragfairEnabled")]
    public bool RagfairEnabled { get; set; } = true;

    /// <summary>
    /// Trader's ruble balance.
    /// </summary>
    [JsonPropertyName("balanceRub")]
    public long BalanceRub { get; set; } = 5000000;

    /// <summary>
    /// Trader's dollar balance.
    /// </summary>
    [JsonPropertyName("balanceDol")]
    public long BalanceDol { get; set; } = 0;

    /// <summary>
    /// Trader's euro balance.
    /// </summary>
    [JsonPropertyName("balanceEur")]
    public long BalanceEur { get; set; } = 0;

    /// <summary>
    /// Item category IDs the trader will buy from the player.
    /// If empty, uses a sensible default set of categories.
    /// </summary>
    [JsonPropertyName("buyCategories")]
    public List<string>? BuyCategories { get; set; }

    /// <summary>
    /// Item template IDs that the trader will NOT buy.
    /// </summary>
    [JsonPropertyName("buyProhibitedItems")]
    public List<string>? BuyProhibitedItems { get; set; }

    /// <summary>
    /// Whether this trader offers insurance (default false).
    /// </summary>
    [JsonPropertyName("insuranceEnabled")]
    public bool InsuranceEnabled { get; set; } = false;

    /// <summary>
    /// Whether this trader offers repair services (default false).
    /// </summary>
    [JsonPropertyName("repairEnabled")]
    public bool RepairEnabled { get; set; } = false;

    /// <summary>
    /// Minimum seconds between trader inventory refreshes.
    /// </summary>
    [JsonPropertyName("refreshTimeMin")]
    public int RefreshTimeMin { get; set; } = 1800;

    /// <summary>
    /// Maximum seconds between trader inventory refreshes.
    /// </summary>
    [JsonPropertyName("refreshTimeMax")]
    public int RefreshTimeMax { get; set; } = 7200;

    /// <summary>
    /// Loyalty levels for this trader. Must have at least one.
    /// </summary>
    [JsonPropertyName("loyaltyLevels")]
    public List<LoyaltyLevelDefinition> LoyaltyLevels { get; set; } = [];

    /// <summary>
    /// Items this trader sells / barters.
    /// </summary>
    [JsonPropertyName("assort")]
    public List<AssortItemDefinition> Assort { get; set; } = [];
}

/// <summary>
/// Defines a single loyalty level for a trader.
/// </summary>
public class LoyaltyLevelDefinition
{
    /// <summary>
    /// Loyalty level number (1-4 typically).
    /// </summary>
    [JsonPropertyName("level")]
    public int Level { get; set; } = 1;

    /// <summary>
    /// Minimum player level required to reach this loyalty level.
    /// </summary>
    [JsonPropertyName("minLevel")]
    public int MinLevel { get; set; } = 1;

    /// <summary>
    /// Minimum total sales sum (roubles spent) required.
    /// </summary>
    [JsonPropertyName("minSalesSum")]
    public long MinSalesSum { get; set; } = 0;

    /// <summary>
    /// Minimum standing/reputation required.
    /// </summary>
    [JsonPropertyName("minStanding")]
    public double MinStanding { get; set; } = 0;

    /// <summary>
    /// Buy price coefficient (percentage the trader pays when buying from player).
    /// Higher = trader pays more. Typical range 30-60.
    /// </summary>
    [JsonPropertyName("buyPriceCoef")]
    public int BuyPriceCoef { get; set; } = 40;
}

/// <summary>
/// Defines a single item or barter offer in the trader's assortment.
/// </summary>
public class AssortItemDefinition
{
    /// <summary>
    /// The SPT item template ID (e.g. "544a11ac4bdc2d470e8b456a").
    /// </summary>
    [JsonPropertyName("itemTpl")]
    public string ItemTpl { get; set; } = string.Empty;

    /// <summary>
    /// Optional fixed item ID. If omitted, one is auto-generated.
    /// Useful for weapon presets where child items reference this ID.
    /// </summary>
    [JsonPropertyName("itemId")]
    public string? ItemId { get; set; }

    /// <summary>
    /// Which loyalty level this item is available at (1-based).
    /// </summary>
    [JsonPropertyName("loyaltyLevel")]
    public int LoyaltyLevel { get; set; } = 1;

    /// <summary>
    /// Stock count. Only matters if unlimitedStock is false.
    /// </summary>
    [JsonPropertyName("stock")]
    public int Stock { get; set; } = 999999;

    /// <summary>
    /// Whether the item has unlimited stock.
    /// </summary>
    [JsonPropertyName("unlimitedStock")]
    public bool UnlimitedStock { get; set; } = true;

    /// <summary>
    /// Price in the specified currency. Ignored if barter items are specified.
    /// </summary>
    [JsonPropertyName("price")]
    public int Price { get; set; } = 0;

    /// <summary>
    /// Currency for the price: "RUB", "USD", or "EUR".
    /// Uses the trader's default currency if omitted.
    /// </summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    /// <summary>
    /// Barter requirements. If specified, this is a barter trade instead of a money purchase.
    /// Multiple barter items = all are required together.
    /// </summary>
    [JsonPropertyName("barter")]
    public List<BarterRequirement>? Barter { get; set; }

    /// <summary>
    /// Maximum number a player can buy per restock (0 = no limit).
    /// </summary>
    [JsonPropertyName("buyLimit")]
    public int BuyLimit { get; set; } = 0;
}

/// <summary>
/// A single barter ingredient requirement.
/// </summary>
public class BarterRequirement
{
    /// <summary>
    /// Item template ID of the required barter item.
    /// </summary>
    [JsonPropertyName("itemTpl")]
    public string ItemTpl { get; set; } = string.Empty;

    /// <summary>
    /// How many of this item are required.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; set; } = 1;
}
