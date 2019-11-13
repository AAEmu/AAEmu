using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers.Id
{
    public class PrivateBookIdManager : IdManager
    {
        private static PrivateBookIdManager _instance;
        private const uint FirstId = 0x00001000;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };

        public static PrivateBookIdManager Instance => _instance ?? (_instance = new PrivateBookIdManager());

        public PrivateBookIdManager() : base("PrivateBookIdManager", FirstId, LastId, Exclude)
        {
        }
        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct)
        {
            using (var ctx = new GameDBContext())
            {
                return ctx.PortalBookCoords.Select(i => i.Id).ToList();
            }
        }
    }
}
