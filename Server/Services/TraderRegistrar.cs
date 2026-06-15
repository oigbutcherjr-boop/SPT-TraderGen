using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Routers;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services;
using SPTarkov.Server.Core.Utils.Cloners;
using TraderGen.Models;
using Path = System.IO.Path;

namespace TraderGen.Services;

// Registers loaded trader definitions into the SPT database.
// Handles: base data, assort, barter schemes, loyalty levels, locales, images, ragfair, refresh config.
[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class TraderRegistrar(
    ISptLogger<TraderRegistrar> logger,
    ICloner cloner,
    DatabaseService databaseService,
    ImageRouter imageRouter,
    ConfigServer configServer,
    ModHelper modHelper
)
{
    private readonly TraderConfig _traderConfig = configServer.GetConfig<TraderConfig>();
    private readonly RagfairConfig _ragfairConfig = configServer.GetConfig<RagfairConfig>();

    // Default buy categories if the trader pack doesn't specify any.
    // These cover common item types (weapons, ammo, gear, meds, etc.)
    private static readonly List<string> DefaultBuyCategories =
    [
        "5d1c819a86f774771b0acd6c", // Weapon parts
    ];

    // Register a single trader from a loaded trader definition.
    public bool RegisterTrader(TraderLoader.LoadedTrader loaded)
    {
        var trader = loaded.Definition;
        var packFolder = loaded.PackFolder;

        try
        {
            // Build the TraderBase object
            var traderBase = BuildTraderBase(trader, packFolder);

            // Register the avatar image route
            RegisterAvatar(trader, traderBase, packFolder);

            // Set trader refresh/update time in config
            SetTraderUpdateTime(traderBase, trader.RefreshTimeMin, trader.RefreshTimeMax);

            // Enable ragfair if requested
            if (trader.RagfairEnabled)
            {
                _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
            }

            // Add the trader with empty assort to the database
            AddTraderToDatabase(traderBase);

            // Add locale entries (name, description, etc.)
            AddTraderLocales(traderBase, trader);

            // Build and assign the assort (items, barter schemes, loyalty levels)
            BuildAndAssignAssort(trader);

            logger.LogWithColor(
                $"[TraderGen] Successfully registered trader '{trader.Nickname}' (ID: {trader.Id})",
                LogTextColor.Green
            );
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWithColor(
                $"[TraderGen] Failed to register trader '{trader.Nickname}': {ex.Message}",
                LogTextColor.Red
            );
            return false;
        }
    }

    // Build the TraderBase object from the simplified trader definition.
    private TraderBase BuildTraderBase(TraderDefinition trader, string packFolder)
    {
        var buyCategories = trader.BuyCategories ?? DefaultBuyCategories;
        var buyProhibited = trader.BuyProhibitedItems ?? new List<string>();
        var currency = trader.Currency.ToUpperInvariant() switch
        {
            "USD" or "DOLLARS" or "DOLLAR" => "USD",
            "EUR" or "EUROS" or "EURO" => "EUR",
            _ => "RUB"
        };

        // Build the base.json content matching SPT's TraderBase format
        var baseJson = new
        {
            _id = trader.Id,
            availableInRaid = false,
            avatar = $"/files/trader/avatar/{trader.Id}.jpg",
            balance_rub = trader.BalanceRub,
            balance_dol = trader.BalanceDol,
            balance_eur = trader.BalanceEur,
            buyer_up = trader.BuyerEnabled,
            currency = currency,
            customization_seller = false,
            discount = 0,
            discount_end = 0,
            gridHeight = 500,
            insurance = new
            {
                availability = trader.InsuranceEnabled,
                min_payment = 0,
                min_return_hour = 0,
                max_return_hour = 1,
                max_storage_time = 144,
                excluded_category = Array.Empty<string>(),
            },
            isCanTransferItems = false,
            items_buy = new
            {
                category = buyCategories,
                id_list = Array.Empty<string>(),
            },
            items_buy_prohibited = new
            {
                category = Array.Empty<string>(),
                id_list = buyProhibited,
            },
            location = trader.Location,
            medic = false,
            name = trader.FullName ?? $"{trader.Nickname} {trader.LastName}",
            nextResupply = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            nickname = trader.Nickname,
            prohibitedTransferableItems = new
            {
                category = Array.Empty<string>(),
                id_list = Array.Empty<string>(),
            },
            repair = new
            {
                availability = trader.RepairEnabled,
                currency = CurrencyHelper.Roubles,
                currency_coefficient = 1,
                excluded_category = Array.Empty<string>(),
                excluded_id_list = Array.Empty<string>(),
                quality = "2",
            },
            sell_category = Array.Empty<string>(),
            sell_modifier_for_prohibited_items = 0,
            surname = trader.LastName,
            transferableItems = new
            {
                category = Array.Empty<string>(),
                id_list = Array.Empty<string>(),
            },
            unlockedByDefault = trader.UnlockedByDefault,
            loyaltyLevels = trader.LoyaltyLevels
                .OrderBy(ll => ll.Level)
                .Select(ll => new
                {
                    minLevel = ll.MinLevel,
                    minSalesSum = ll.MinSalesSum,
                    minStanding = ll.MinStanding,
                    buy_price_coef = ll.BuyPriceCoef,
                    repair_price_coef = 150,
                    insurance_price_coef = 10,
                    exchange_price_coef = 0,
                    heal_price_coef = 0,
                })
                .ToArray(),
        };

        // Serialize to JSON and write to a temp file in the pack folder
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        var jsonContent = JsonSerializer.Serialize(baseJson, jsonOptions);
        var baseFilePath = Path.Combine(packFolder, $".tradergen_base_{trader.Id}.json");
        File.WriteAllText(baseFilePath, jsonContent);

        // Load it using SPT's ModHelper which deserializes via Newtonsoft with correct type mapping
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(packFolder, $".tradergen_base_{trader.Id}.json");

        // Clean up the temp file
        try { File.Delete(baseFilePath); } catch { /* ignore */ }

        return traderBase;
    }

    // Register the trader's avatar image with the SPT image router.
    // Looks for the avatar file relative to the trader pack folder.
    private void RegisterAvatar(TraderDefinition trader, TraderBase traderBase, string packFolder)
    {
        var avatarRelPath = trader.Avatar.Replace('/', Path.DirectorySeparatorChar);
        var avatarAbsPath = Path.Combine(packFolder, avatarRelPath);

        if (!File.Exists(avatarAbsPath))
        {
            logger.LogWithColor(
                $"[TraderGen] Warning: Avatar file not found at '{avatarAbsPath}'. Trader will have no image.",
                LogTextColor.Yellow
            );
            return;
        }

        // The route must match the avatar path in the base (without extension)
        var routePath = traderBase.Avatar.Replace(".jpg", "");
        imageRouter.AddRoute(routePath, avatarAbsPath);
    }

    // Configure how often the trader's inventory refreshes.
    private void SetTraderUpdateTime(TraderBase traderBase, int minSeconds, int maxSeconds)
    {
        var updateTime = new UpdateTime
        {
            TraderId = traderBase.Id,
            Seconds = new MinMax<int>(minSeconds, maxSeconds),
        };
        _traderConfig.UpdateTime.Add(updateTime);
    }

    // Add the trader to the database with an empty assort (assort is filled later).
    private void AddTraderToDatabase(TraderBase traderBase)
    {
        var emptyAssort = new TraderAssort
        {
            Items = [],
            BarterScheme = new Dictionary<MongoId, List<List<BarterScheme>>>(),
            LoyalLevelItems = new Dictionary<MongoId, int>(),
        };

        var traderData = new Trader
        {
            Assort = emptyAssort,
            Base = cloner.Clone(traderBase),
            QuestAssort = new()
            {
                { "Started", new() },
                { "Success", new() },
                { "Fail", new() },
            },
            Dialogue = [],
        };

        if (!databaseService.GetTables().Traders.TryAdd(traderBase.Id, traderData))
        {
            logger.LogWithColor(
                $"[TraderGen] Warning: Trader ID '{traderBase.Id}' already exists in database. Skipping.",
                LogTextColor.Yellow
            );
        }
    }

    // Add locale entries for all languages so the trader's name and description display correctly.
    private void AddTraderLocales(TraderBase traderBase, TraderDefinition trader)
    {
        var locales = databaseService.GetTables().Locales.Global;
        var traderId = traderBase.Id;

        foreach (var (localeKey, localeKvP) in locales)
        {
            localeKvP.AddTransformer(localeData =>
            {
                localeData.TryAdd($"{traderId} FullName", trader.FullName ?? $"{trader.Nickname} {trader.LastName}");
                localeData.TryAdd($"{traderId} FirstName", trader.FirstName);
                localeData.TryAdd($"{traderId} Nickname", trader.Nickname);
                localeData.TryAdd($"{traderId} Location", trader.Location);
                localeData.TryAdd($"{traderId} Description", trader.Description);
                return localeData;
            });
        }
    }

    // Build the trader's assort (items for sale) and assign it to the database.
    // Each assort item gets: an Item entry, a BarterScheme entry, and a LoyalLevelItems entry.
    private void BuildAndAssignAssort(TraderDefinition trader)
    {
        var traderData = databaseService.GetTables().Traders.GetValueOrDefault(trader.Id);
        if (traderData == null)
        {
            logger.LogWithColor($"[TraderGen] Cannot build assort: trader '{trader.Id}' not found in database.", LogTextColor.Red);
            return;
        }

        foreach (var assortItem in trader.Assort)
        {
            try
            {
                // Generate or use the specified item ID
                var itemId = assortItem.ItemId ?? new MongoId().ToString();

                // Create the root item (parentId = "hideout" marks it as a top-level assort item)
                var item = new Item
                {
                    Id = itemId,
                    Template = assortItem.ItemTpl,
                    ParentId = "hideout",
                    SlotId = "hideout",
                    Upd = new Upd
                    {
                        UnlimitedCount = assortItem.UnlimitedStock,
                        StackObjectsCount = assortItem.Stock,
                    },
                };

                // Apply buy restriction if specified
                if (assortItem.BuyLimit > 0)
                {
                    item.Upd.BuyRestrictionMax = assortItem.BuyLimit;
                    item.Upd.BuyRestrictionCurrent = 0;
                }

                traderData.Assort.Items.Add(item);

                // Build the barter scheme (what the player pays)
                var barterSchemeList = BuildBarterScheme(assortItem, trader.Currency);
                traderData.Assort.BarterScheme[itemId] = barterSchemeList;

                // Set the loyalty level requirement
                traderData.Assort.LoyalLevelItems[itemId] = assortItem.LoyaltyLevel;
            }
            catch (Exception ex)
            {
                logger.LogWithColor(
                    $"[TraderGen] Error adding assort item '{assortItem.ItemTpl}' to trader '{trader.Nickname}': {ex.Message}",
                    LogTextColor.Red
                );
            }
        }
    }

    // Build the barter scheme for a single assort item.
    // If barter requirements are specified, use those. Otherwise, use money price.
    private List<List<BarterScheme>> BuildBarterScheme(AssortItemDefinition assortItem, string defaultCurrency)
    {
        var schemeItems = new List<BarterScheme>();

        if (assortItem.Barter is { Count: > 0 })
        {
            // Barter trade: each requirement is an ingredient
            foreach (var barter in assortItem.Barter)
            {
                schemeItems.Add(new BarterScheme
                {
                    Template = barter.ItemTpl,
                    Count = barter.Count,
                });
            }
        }
        else
        {
            // Money purchase
            var currencyTpl = CurrencyHelper.ToTemplateId(assortItem.Currency ?? defaultCurrency);
            schemeItems.Add(new BarterScheme
            {
                Template = currencyTpl,
                Count = assortItem.Price,
            });
        }

        // SPT expects List<List<BarterScheme>> — outer list is OR options, inner list is AND requirements
        // For simplicity, we only support a single option with all requirements AND'd together
        return [schemeItems];
    }
}
