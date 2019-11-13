using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers.Id
{
    public class VisitedSubZoneIdManager : IdManager
    {
        private static VisitedSubZoneIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };

        public static VisitedSubZoneIdManager Instance => _instance ?? (_instance = new VisitedSubZoneIdManager());

        public VisitedSubZoneIdManager() : base("VisitedSubZoneIdMananger", FirstId, LastId, Exclude)
        {
        }

        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct)
        {
            using (var ctx = new GameDBContext())
            {
                return ctx.PortalVisitedDistrict.Select(i => i.Id).ToList();
            }
        }
    }
}
