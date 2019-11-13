using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Utils;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ItemIdManager : IdManager
    {
        private static ItemIdManager _instance;
        private const uint FirstId = 0x01000000;
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };

        public static ItemIdManager Instance => _instance ?? (_instance = new ItemIdManager());

        public ItemIdManager() : base("ItemIdManager", FirstId, LastId, Exclude)
        {
        }
        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct)
        {
            using (var ctx = new GameDBContext())
            {
                return ctx.Items.Select(i => i.Id).ToList().Select(i=>(uint)i).ToList();
            }
        }
    }
}
