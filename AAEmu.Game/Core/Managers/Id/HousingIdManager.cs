using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class HousingIdManager : IdManager
    {
        private static HousingIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"housings", "id"}};

        public static HousingIdManager Instance => _instance ?? (_instance = new HousingIdManager());

        public HousingIdManager() : base("HousingIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
