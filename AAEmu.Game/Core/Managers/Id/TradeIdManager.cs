using System.Collections.Generic;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class TradeIdManager : IdManager
    {
        private static TradeIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };

        public static TradeIdManager Instance => _instance ?? (_instance = new TradeIdManager());

        public TradeIdManager() : base("TradeIdManager", FirstId, LastId, Exclude)
        {
        }

        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct) => new List<uint>();
    }
}
