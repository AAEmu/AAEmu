using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ItemIdManager : IdManager
    {
        private static ItemIdManager _instance;
        private const uint FirstId = 0x01000000;
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "items", "id" } };

        public static ItemIdManager Instance => _instance ?? (_instance = new ItemIdManager());

        public ItemIdManager() : base("ItemIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}