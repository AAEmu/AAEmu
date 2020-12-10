using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game;
using NLog;
using AAEmu.Game.Models.Game.Features;

namespace AAEmu.Game.Core.Managers
{
    public class FeaturesManager : Singleton<FeaturesManager>
    {
        public static FeatureSet Fsets;
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        public void Initialize()
        {
            _log.Info("Initializing Features ...");
            Fsets = new FeatureSet();

            //Disables Auction Button
            //Set(Feature.hudAuctionButton, false);
            //Enables family invites
            Fsets.Set(Feature.allowFamilyChanges, true);
            //Disables Dwarf/Warborn character creation (0.5 only)
            Fsets.Set(Feature.dwarfWarborn, false);

            // Debug convenience flags
            Fsets.Set(Feature.sensitiveOpeartion, false);
            Fsets.Set(Feature.secondpass, false);
            Fsets.Set(Feature.ingameshopSecondpass, false);
            Fsets.Set(Feature.itemSecure, false);

            //Fsets.Set(Feature.taxItem, false);

            var featsOn = string.Empty;
            foreach (var fObj in Enum.GetValues(typeof(Feature)))
            {
                var f = (Feature)fObj;
                if (FeaturesManager.Fsets.Check(f))
                    featsOn += f.ToString() + "  ";
            }
            _log.Info("Enabled Features: {0}",featsOn);
        }

    }
}
