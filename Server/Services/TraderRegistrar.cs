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

// Registers trader definitions into the SPT database.
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

    // Default buy categories.
    private static readonly List<string> DefaultBuyCategories =
    [
        "5d1c819a86f774771b0acd6c", // Weapon parts
    ];

    // Register a single trader.
    public bool RegisterTrader(TraderLoader.LoadedTrader loaded)
    {
        var trader = loaded.Definition;
        var packFolder = loaded.PackFolder;

        try
        {
            // Build TraderBase
            var traderBase = BuildTraderBase(trader, packFolder);

            // Register avatar
            RegisterAvatar(trader, traderBase, packFolder);

            // Set refresh time
            SetTraderUpdateTime(traderBase, trader.RefreshTimeMin, trader.RefreshTimeMax);

            // Enable ragfair
            if (trader.RagfairEnabled)
            {
                _ragfairConfig.Traders.TryAdd(traderBase.Id, true);
            }

            // Add trader to database
            AddTraderToDatabase(traderBase);

            // Add locale entries
            AddTraderLocales(traderBase, trader);

            // Build and assign assort
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

    // Build TraderBase from trader definition.
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

        // Build base.json content
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
            sell_category = trader.SellCategories?.Count > 0
                ? trader.SellCategories
                :
                [
                    // Handbook category IDs (used by SPT client to filter trader items)
                    "5b47574386f77428ca22b33e", // Barter / Loot
                    "5b47574386f77428ca22b33f", // Gear (armour, rigs, helmets, backpacks, etc.)
                    "5b47574386f77428ca22b346", // Ammo
                    "5b47574386f77428ca22b345", // Special equipment
                    "5b47574386f77428ca22b343", // Maps
                    "5b5f71b386f774093f2ecf11", // Weapons – assault rifles
                    "5b5f71c186f77409407a7ec0", // Weapons – assault carbines
                    "5b5f71de86f774093f2ecf13", // Weapons – machine guns
                    "5b5f724186f77447ed5636ad", // Weapons – SMGs
                    "5b5f736886f774094242f193", // Weapons – shotguns
                    "5b5f73ec86f774093e6cb4fd", // Weapons – pistols
                    "5b5f74cc86f77447ec5d770a", // Weapons – marksman rifles
                    "5b5f750686f774093e6cb503", // Weapons – sniper rifles
                    "5b5f751486f77447ec5d770c", // Weapons – grenade launchers
                    "5b5f752e86f774093e6cb505", // Weapons – special weapons
                    "5b5f754a86f774094242f19b", // Weapons – melee
                    "5b5f755f86f77447ec5d770e", // Weapons – throwables
                    "5b5f757486f774093e6cb507", // Weapon mods – functional
                    "5b5f75b986f77447ec5d7710", // Weapon mods – gear mods
                    "5b5f75c686f774094242f19f", // Weapon mods – muzzle
                    "5b5f75e486f77447ec5d7712", // Weapon mods – sights
                    "5b5f760586f774093e6cb509", // Weapon mods – magazine
                    "5b5f761f86f774094242f1a1", // Weapon mods – stock
                    "575146b724597720a27126d5", // Weapon mods – barrel
                    "635a758bfefc88a93f021b8a", // Weapon mods – handguard
                    "55d45d3f4bdc2d972f8b456c", // Weapon mods – mount
                    "5b363dd25acfc4001a598fd2", // Weapon mods – charging handle
                    "5d1b36a186f7742523398433", // Weapon mods – launcher
                    "59e3577886f774176a362503", // Weapon mods – bipod
                    "5d6e67fba4b9361bc73bc779", // Weapon mods – foregrip
                    "5b5f764186f77447ec5d7714", // Weapon mods – tactical
                ],
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

        // Serialize to temp file
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = null,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        var jsonContent = JsonSerializer.Serialize(baseJson, jsonOptions);
        var baseFilePath = Path.Combine(packFolder, $".tradergen_base_{trader.Id}.json");
        File.WriteAllText(baseFilePath, jsonContent);

        // Load via SPT ModHelper
        var traderBase = modHelper.GetJsonDataFromFile<TraderBase>(packFolder, $".tradergen_base_{trader.Id}.json");

        // Clean up temp file
        try { File.Delete(baseFilePath); } catch { /* ignore */ }

        return traderBase;
    }

    // Register avatar image.
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

        // Route must match avatar path
        var routePath = traderBase.Avatar.Replace(".jpg", "");
        imageRouter.AddRoute(routePath, avatarAbsPath);
    }

    // Configure inventory refresh time.
    private void SetTraderUpdateTime(TraderBase traderBase, int minSeconds, int maxSeconds)
    {
        var updateTime = new UpdateTime
        {
            TraderId = traderBase.Id,
            Seconds = new MinMax<int>(minSeconds, maxSeconds),
        };
        _traderConfig.UpdateTime.Add(updateTime);
    }

    // Add trader to database with empty assort.
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

    // Add locale entries for all languages.
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

    // Build trader's assort and assign to database.
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
                // Generate or use specified item ID
                var itemId = assortItem.ItemId ?? new MongoId().ToString();

                // Create root item
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

                // Apply buy restriction
                if (assortItem.BuyLimit > 0)
                {
                    item.Upd.BuyRestrictionMax = assortItem.BuyLimit;
                    item.Upd.BuyRestrictionCurrent = 0;
                }

                traderData.Assort.Items.Add(item);

                // Recursively flatten child tree into flat item list
                if (assortItem.Children is { Count: > 0 })
                {
                    FlattenChildren(assortItem.Children, itemId, traderData.Assort.Items);
                }

                // Build barter scheme
                var barterSchemeList = BuildBarterScheme(assortItem, trader.Currency);
                traderData.Assort.BarterScheme[itemId] = barterSchemeList;

                // Set loyalty level
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

    // Build barter scheme for an assort item.
    private List<List<BarterScheme>> BuildBarterScheme(AssortItemDefinition assortItem, string defaultCurrency)
    {
        var schemeItems = new List<BarterScheme>();

        if (assortItem.Barter is { Count: > 0 })
        {
            // Barter trade
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

        // SPT expects List<List<BarterScheme>>
        return [schemeItems];
    }

    // Recursively walk a tree of AssortChildItem and flatten into a flat SPT Item list.
    // Each child's parentId is set to the ID of the item it attaches to.
    private void FlattenChildren(List<AssortChildItem> children, string parentId, List<Item> items)
    {
        foreach (var child in children)
        {
            var childId = child.ItemId ?? new MongoId().ToString();
            items.Add(new Item
            {
                Id = childId,
                Template = child.ItemTpl,
                ParentId = parentId,
                SlotId = child.SlotId,
                Upd = new Upd(),
            });

            if (child.Children is { Count: > 0 })
            {
                FlattenChildren(child.Children, childId, items);
            }
        }
    }
}
