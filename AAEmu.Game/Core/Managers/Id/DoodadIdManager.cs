using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class DoodadIdManager : IdManager
    {
        private static DoodadIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"doodads", "id"}};

        public static DoodadIdManager Instance => _instance ?? (_instance = new DoodadIdManager());

        public DoodadIdManager() : base("DoodadIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
