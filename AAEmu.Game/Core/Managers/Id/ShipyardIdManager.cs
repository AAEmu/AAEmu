using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ShipyardIdManager : IdManager
    {
        private static ShipyardIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "shipyards", "id" } };

        public static ShipyardIdManager Instance => _instance ?? (_instance = new ShipyardIdManager());

        public ShipyardIdManager() : base("ShipyardIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
