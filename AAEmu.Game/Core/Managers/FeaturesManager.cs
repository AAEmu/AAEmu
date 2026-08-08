using AAEmu.Commons.Utils;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Features;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FeaturesManager(IExperienceManager experienceManager) : Singleton<FeaturesManager>, IFeaturesManager
{
    public static FeatureSet Fsets { get; private set; }

    /// <summary>
    /// Whether Heir (ancestral) progression runs on this server.
    /// </summary>
    /// <remarks>
    /// The rest of the fset only advertises to the client, but heir cannot be left to that: heir
    /// experience accrues in <c>AddExp</c> and <c>HeirLevel</c> derives from it, and that level is
    /// broadcast in UnitState, team, friend, expedition and family packets and folded into the
    /// <c>Level + HeirLevel</c> gates for trade, auction, chat and mail. Advertising the feature as
    /// off while still accumulating it would diverge visible state from the configuration, and would
    /// hand every level-capped character an unearned pile of heir levels the moment it was switched
    /// on. So the same bits gate the server: progression and the Heir C2G handlers both consult this.
    /// Both are required, matching the client - useHeirSkill (202) reveals the tab, heirLevel (101)
    /// the level block within it.
    /// <para>
    /// This gates what happens next, not what already happened. <c>characters.heir_exp</c> and
    /// <c>heir_skill_activations</c> are untouched while it is false, so <see cref="Character.HeirLevel"/>
    /// still resolves from the stored total and successors already chosen stay active; turning the
    /// bits back on resumes from there. Deliberate: silently voiding earned levels and equipped
    /// skills would be worse than leaving them dormant.
    /// </para>
    /// </remarks>
    public static bool HeirEnabled =>
        Fsets is not null && Fsets.Check(Feature.useHeirSkill) && Fsets.Check(Feature.heirLevel);

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Initialize()
    {
        Logger.Info("Initializing Features ...");

        // Every bit starts cleared and is turned on from Configurations/Features.json, so the blob only
        // advertises what this server answers: an enabled bit opens client UI and lets the client send
        // packets we would have to drop.
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
