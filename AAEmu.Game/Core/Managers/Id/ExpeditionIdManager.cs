using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ExpeditionIdManager : IdManager
    {
        private static ExpeditionIdManager _instance;
        private const uint FirstId = 1000; // Based on official packets
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };

        public static ExpeditionIdManager Instance => _instance ?? (_instance = new ExpeditionIdManager());

        public ExpeditionIdManager() : base("ExpeditionIdManager", FirstId, LastId, Exclude)
        {
        }

        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct)
        {
            using (var ctx = new GameDBContext())
            {
                return ctx.Expeditions.Select(i => i.Id).ToList();
            }
        }
    }
}
