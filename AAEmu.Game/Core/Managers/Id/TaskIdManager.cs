using System.Collections.Generic;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class TaskIdManager : IdManager
    {
        private static TaskIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x7FFFFFFF;
        private static readonly uint[] Exclude = { };

        public static TaskIdManager Instance => _instance ?? (_instance = new TaskIdManager());

        public TaskIdManager()
            : base("TaskIdManager", FirstId, LastId, Exclude)
        {

        }

        protected override IEnumerable<uint> ExtractUsedIds(bool isDistinct) => new List<uint>();
    }
}
