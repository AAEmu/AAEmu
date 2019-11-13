using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers.Id
{
    public class FamilyIdManager : IdManager
    {
        private static FamilyIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };

        public static FamilyIdManager Instance => _instance ?? (_instance = new FamilyIdManager());

        public FamilyIdManager() : base("FamilyIdManager", FirstId, LastId, Exclude, true)
        {
        }

        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct)
        {
            using (var ctx = new GameDBContext())
            {
                return ctx.Characters.Select(i => i.Family).ToList();
            }
        }
    }
}
