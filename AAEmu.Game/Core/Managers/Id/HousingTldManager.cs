using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class HousingTldManager : IdManager
    {
        private static HousingTldManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x0000FFFE;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{ }};

        public static HousingTldManager Instance => _instance ?? (_instance = new HousingTldManager());

        public HousingTldManager() : base("HousingTldManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
