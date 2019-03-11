using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ExpeditionIdManager : IdManager
    {
        private static ExpeditionIdManager _instance;
        private const uint FirstId = 1000; // Based on official packets
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"expeditions", "id"}};

        public static ExpeditionIdManager Instance => _instance ?? (_instance = new ExpeditionIdManager());

        public ExpeditionIdManager() : base("ExpeditionIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
