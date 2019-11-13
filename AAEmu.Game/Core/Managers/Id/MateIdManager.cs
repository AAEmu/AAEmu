using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers.Id
{
    public class MateIdManager : IdManager
    {
        private static MateIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };

        public static MateIdManager Instance => _instance ?? (_instance = new MateIdManager());

        public MateIdManager() : base("MateIdManager", FirstId, LastId, Exclude)
        {
        }
        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct)
        {
            using (var ctx = new GameDBContext())
            {
                return ctx.Mates.Select(i => i.Id).ToList();
            }
        }
    }
}
