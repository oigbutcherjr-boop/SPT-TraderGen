using TraderGen.Models;

namespace TraderGen.Validation;


// Validates TraderDefinition objects and returns a list of error messages, designed to give clear, actionable feedback for non-programmers.

public static class TraderValidator
{
    public static List<string> Validate(TraderDefinition trader, string sourceFile)
    {
        var errors = new List<string>();
        var prefix = $"[{sourceFile}]";

        // --- ID validation ---
        if (string.IsNullOrWhiteSpace(trader.Id))
        {
            errors.Add($"{prefix} 'id' is required. Use a 24-character hex string (e.g. MongoDB ObjectId).");
        }
        else if (trader.Id.Length != 24 || !IsHexString(trader.Id))
        {
            errors.Add($"{prefix} 'id' must be a 24-character hexadecimal string. Got: '{trader.Id}'");
        }

        // --- Nickname ---
        if (string.IsNullOrWhiteSpace(trader.Nickname))
        {
            errors.Add($"{prefix} 'nickname' is required. This is the trader's display name.");
        }

        // --- First name ---
        if (string.IsNullOrWhiteSpace(trader.FirstName))
        {
            errors.Add($"{prefix} 'firstName' is required.");
        }

        // --- Avatar ---
        if (string.IsNullOrWhiteSpace(trader.Avatar))
        {
            errors.Add($"{prefix} 'avatar' is required. Provide a relative path to the avatar image (e.g. 'assets/avatar.jpg').");
        }

        // --- Currency ---
        if (!CurrencyHelper.IsValid(trader.Currency))
        {
            errors.Add($"{prefix} 'currency' is invalid: '{trader.Currency}'. Use 'RUB', 'USD', or 'EUR'.");
        }

        // --- Unlock quest ---
        if (!trader.UnlockedByDefault && !string.IsNullOrWhiteSpace(trader.UnlockQuestId))
        {
            if (trader.UnlockQuestId.Length != 24 || !IsHexString(trader.UnlockQuestId))
            {
                errors.Add($"{prefix} 'unlockQuestId' must be a 24-character hex string. Got: '{trader.UnlockQuestId}'");
            }
        }

        // --- Loyalty levels ---
        if (trader.LoyaltyLevels.Count == 0)
        {
            errors.Add($"{prefix} At least one loyalty level is required.");
        }
        else
        {
            var seenLevels = new HashSet<int>();
            foreach (var ll in trader.LoyaltyLevels)
            {
                if (ll.Level < 1 || ll.Level > 10)
                {
                    errors.Add($"{prefix} Loyalty level {ll.Level} is out of range (1-10).");
                }

                if (!seenLevels.Add(ll.Level))
                {
                    errors.Add($"{prefix} Duplicate loyalty level: {ll.Level}.");
                }

                if (ll.MinLevel < 1)
                {
                    errors.Add($"{prefix} Loyalty level {ll.Level}: 'minLevel' must be >= 1.");
                }

                if (ll.MinSalesSum < 0)
                {
                    errors.Add($"{prefix} Loyalty level {ll.Level}: 'minSalesSum' cannot be negative.");
                }
            }
        }

        // --- Assort items ---
        foreach (var (item, index) in trader.Assort.Select((item, i) => (item, i)))
        {
            var itemPrefix = $"{prefix} Assort[{index}]";

            if (string.IsNullOrWhiteSpace(item.ItemTpl))
            {
                errors.Add($"{itemPrefix}: 'itemTpl' is required. This is the item's template ID from the SPT database.");
            }
            else if (item.ItemTpl.Length != 24 || !IsHexString(item.ItemTpl))
            {
                errors.Add($"{itemPrefix}: 'itemTpl' must be a 24-character hex string. Got: '{item.ItemTpl}'");
            }

            if (item.LoyaltyLevel < 1)
            {
                errors.Add($"{itemPrefix}: 'loyaltyLevel' must be >= 1.");
            }
            else if (!trader.LoyaltyLevels.Any(ll => ll.Level == item.LoyaltyLevel))
            {
                errors.Add($"{itemPrefix}: 'loyaltyLevel' {item.LoyaltyLevel} does not match any defined loyalty level.");
            }

            if (item.Stock < 0)
            {
                errors.Add($"{itemPrefix}: 'stock' cannot be negative.");
            }

            // Validate price OR barter (at least one must be specified)
            var hasBarter = item.Barter is { Count: > 0 };
            var hasPrice = item.Price > 0;

            if (!hasBarter && !hasPrice)
            {
                errors.Add($"{itemPrefix}: Must specify either a 'price' > 0 or at least one 'barter' requirement.");
            }

            if (hasPrice && hasBarter)
            {
                // This is technically okay - price is ignored when barter exists.
                // But warn the user.
                errors.Add($"{itemPrefix}: Both 'price' and 'barter' are specified. The 'price' will be ignored in favor of the barter.");
            }

            if (item.Currency != null && !CurrencyHelper.IsValid(item.Currency))
            {
                errors.Add($"{itemPrefix}: Invalid currency '{item.Currency}'. Use 'RUB', 'USD', or 'EUR'.");
            }

            if (!string.IsNullOrWhiteSpace(item.LockedByQuest) && (item.LockedByQuest.Length != 24 || !IsHexString(item.LockedByQuest)))
            {
                errors.Add($"{itemPrefix}: 'lockedByQuest' must be a 24-character hex string. Got: '{item.LockedByQuest}'");
            }

            // Validate barter items
            if (hasBarter)
            {
                foreach (var (barter, bIdx) in item.Barter!.Select((b, i) => (b, i)))
                {
                    if (string.IsNullOrWhiteSpace(barter.ItemTpl))
                    {
                        errors.Add($"{itemPrefix}.barter[{bIdx}]: 'itemTpl' is required.");
                    }
                    else if (barter.ItemTpl.Length != 24 || !IsHexString(barter.ItemTpl))
                    {
                        errors.Add($"{itemPrefix}.barter[{bIdx}]: 'itemTpl' must be a 24-character hex string. Got: '{barter.ItemTpl}'");
                    }

                    if (barter.Count < 1)
                    {
                        errors.Add($"{itemPrefix}.barter[{bIdx}]: 'count' must be >= 1.");
                    }
                }
            }
        }

        return errors;
    }

    // Quick check for warnings (non-fatal issues).
    public static List<string> GetWarnings(TraderDefinition trader, string sourceFile)
    {
        var warnings = new List<string>();
        var prefix = $"[{sourceFile}]";

        if (trader.Assort.Count == 0)
        {
            warnings.Add($"{prefix} Trader has no assort items. The trader will appear but sell nothing.");
        }

        if (trader.LoyaltyLevels.Count == 1)
        {
            warnings.Add($"{prefix} Trader has only 1 loyalty level. Consider adding more for progression.");
        }

        if (!trader.BuyerEnabled)
        {
            warnings.Add($"{prefix} Buyer is disabled. Players cannot sell items to this trader.");
        }

        return warnings;
    }

    private static bool IsHexString(string s)
    {
        return s.All(c => Uri.IsHexDigit(c));
    }
}
