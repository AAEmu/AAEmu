using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class QuestIdManager : IdManager
    {
        private static QuestIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "quests", "id" } };

        public static QuestIdManager Instance => _instance ?? (_instance = new QuestIdManager());

        public QuestIdManager() : base("QuestIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
