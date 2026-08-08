using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Features;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FeaturesManager(IExperienceManager experienceManager) : Singleton<FeaturesManager>, IFeaturesManager
{
    public static FeatureSet Fsets { get; private set; }

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Initialize()
    {
        Logger.Info("Initializing Features ...");

        // Every bit starts cleared and is turned on from Configurations/Features.json. The same set
        // controls server behavior and is serialized to the client in SCInitialConfig.
        var config = AppConfiguration.Instance.Features;

        Fsets = new FeatureSet
        {
            PlayerLevelLimit = experienceManager.MaxPlayerLevel,
            MateLevelLimit = experienceManager.MaxMateLevel,

            // TODO(v10): fset[26] publishes the butler level cap. The butler system exists only as packet
            // classes (CSRequestButlerHarvestJobPacket, SCButlerSpawnedPacket); nothing on the server
            // tracks a butler or its level, so there is no cap to publish. Feature.butler stays off.
            ButlerLevelLimit = 0,

            // the trade / block_trade_by_nft cluster. Its unit is not established, so no value can be
            // published without guessing at the scale.
            UnknownTimeLimit = 0,

            TaxItem = config.TaxItem,
            BackpackProfitShare = config.BackpackProfitShare
        };

        ApplyConfiguredFlags(config);

        // Distinct(): Feature aliases a few bits (dwarfWarborn == itemChangeMapping), and GetValues
        // returns one entry per name, which would list the same bit twice.
        var featsOn = string.Empty;
        foreach (var f in Enum.GetValues<Feature>().Distinct())
        {
            if (FeatureSet.IsValid(f) && Fsets.Check(f))
                featsOn += f + "  ";
        }

        Logger.Info($"fset: {Fsets}");
        Logger.Info($"Enabled Features: {featsOn}");
    }

    /// <summary>
    /// Applies <c>Features.Flags</c> to the blob. A key that names no <see cref="Feature"/>, or one that
    /// lands in a scalar byte, is a configuration error: it is reported and skipped rather than silently
    /// doing nothing.
    /// </summary>
    private static void ApplyConfiguredFlags(FeaturesConfig config)
    {
        foreach (var (name, enabled) in config.Flags)
        {
            if (!Enum.TryParse<Feature>(name, true, out var feature) || !Enum.IsDefined(feature))
            {
                Logger.Error("Features.Flags: '{0}' is not a feature defined for 10.0.2.13", name);
                continue;
            }

            if (!Fsets.Set(feature, enabled))
                Logger.Error("Features.Flags: '{0}' is bit {1}, which is past the end of the fset or inside a scalar byte",
                    name, (int)feature);
        }
    }
}
