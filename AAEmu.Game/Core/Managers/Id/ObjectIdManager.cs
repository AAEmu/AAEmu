using System.Collections.Generic;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ObjectIdManager : IdManager
    {
        private static ObjectIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFE;
        private static readonly uint[] Exclude = { };

        public static ObjectIdManager Instance => _instance ?? (_instance = new ObjectIdManager());

        public ObjectIdManager() : base("ObjectIdManager", FirstId, LastId, Exclude)
        {
        }

        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct) => new List<uint>();
    }
}
