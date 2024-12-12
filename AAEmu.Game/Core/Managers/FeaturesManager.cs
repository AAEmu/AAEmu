using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Features;
using NLog;

namespace AAEmu.Game.Core.Managers;

public class FeaturesManager : Singleton<FeaturesManager>
{
    public static FeatureSet Fsets { get; private set; }

    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public static void Initialize()
    {
        Logger.Info("Initializing Features ...");
        Fsets = new FeatureSet();

        Fsets.PlayerLevelLimit = ExperienceManager.MaxPlayerLevel;
        Fsets.MateLevelLimit = ExperienceManager.MaxMateLevel;

        // Allow House sales
        Fsets.Set(Feature.houseSale, true);

        // Disables Auction Button
        // Fsets.Set(Feature.hudAuctionButton, false);

        // Enable the Nations UI menu
        Fsets.Set(Feature.nations, true);

        // Enables family invites
        Fsets.Set(Feature.allowFamilyChanges, true);

        // Disables Dwarf/Warborn character creation (0.5 only)
        Fsets.Set(Feature.dwarfWarborn, false);

        // Debug convenience flags, disables most of the sensitive operation stuff to do easier testing
        Fsets.Set(Feature.sensitiveOperation, false);
        Fsets.Set(Feature.secondpass, false);
        Fsets.Set(Feature.ingameshopSecondpass, false);
        Fsets.Set(Feature.itemSecure, false);

        // Use gold instead of tax certificates to pay house tax
        // Fsets.Set(Feature.taxItem, false); 

        // Enable the Custom UI (Addons) button and menu
        Fsets.Set(Feature.customUiButton, true);

        // The following flags are set in our default, but have unknown behaviour. Disabling them seems to have no impact on gameplay
        /*
        Fsets.Set(Feature.flag_2_0, false);
        Fsets.Set(Feature.flag_2_1, false);
        Fsets.Set(Feature.flag_2_2, false);
        Fsets.Set(Feature.flag_2_3, false);

        Fsets.Set(Feature.flag_3_0, false);
        Fsets.Set(Feature.flag_3_1, false);
        Fsets.Set(Feature.flag_3_2, false);
        Fsets.Set(Feature.flag_3_3, false);

        Fsets.Set(Feature.flag_4_0, false);
        Fsets.Set(Feature.flag_4_3, false);
        Fsets.Set(Feature.flag_4_5, false);

        Fsets.Set(Feature.flag_6_1, false);
        */

        var featsOn = string.Empty;
        foreach (var fObj in Enum.GetValues(typeof(Feature)))
        {
            var f = (Feature)fObj;
            if (Fsets.Check(f))
                featsOn += f + "  ";
        }
        Logger.Info($"Enabled Features: {featsOn}");
    }
}
