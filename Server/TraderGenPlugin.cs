using System.Reflection;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Logging;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Utils;
using TraderGen.Services;

namespace TraderGen;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.serenity.tradergen";
    public override string Name { get; init; } = "TraderGen";
    public override string Author { get; init; } = "Serenity";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("1.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("4.0.13");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; }
    public override bool? IsBundleMod { get; init; } = false;
    public override string? License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class TraderGenPlugin(
    ISptLogger<TraderGenPlugin> logger,
    TraderLoader traderLoader,
    TraderRegistrar traderRegistrar
) : IOnLoad
{
    public Task OnLoad()
    {
        logger.LogWithColor("[TraderGen] ====================================", LogTextColor.Cyan);
        logger.LogWithColor("[TraderGen] TraderGen Framework v1.0.0 loading...", LogTextColor.Cyan);
        logger.LogWithColor("[TraderGen] ====================================", LogTextColor.Cyan);

        // Discover and load all trader JSON files from the traders/ directory
        var loadedTraders = traderLoader.LoadAllTraders();

        if (loadedTraders.Count == 0)
        {
            logger.LogWithColor(
                "[TraderGen] No trader packs found. Place trader pack folders in: user/mods/TraderGen/traders/",
                LogTextColor.Yellow
            );
            return Task.CompletedTask;
        }

        logger.LogWithColor($"[TraderGen] Found {loadedTraders.Count} trader definition(s). Registering...", LogTextColor.Cyan);

        var successCount = 0;
        var failCount = 0;

        foreach (var loaded in loadedTraders)
        {
            // Each trader is registered independently — one failure won't crash the others
            var success = traderRegistrar.RegisterTrader(loaded);
            if (success)
            {
                successCount++;
            }
            else
            {
                failCount++;
            }
        }

        logger.LogWithColor("[TraderGen] ====================================", LogTextColor.Cyan);
        logger.LogWithColor(
            $"[TraderGen] Done! {successCount} trader(s) registered, {failCount} failed.",
            failCount > 0 ? LogTextColor.Yellow : LogTextColor.Green
        );
        logger.LogWithColor("[TraderGen] ====================================", LogTextColor.Cyan);

        return Task.CompletedTask;
    }
}
