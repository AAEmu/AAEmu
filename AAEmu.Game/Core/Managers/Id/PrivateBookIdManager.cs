using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class PrivateBookIdManager : IdManager
    {
        private static PrivateBookIdManager _instance;
        private const uint FirstId = 0x00001000;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "portal_book_coords", "id" } };

        public static PrivateBookIdManager Instance => _instance ?? (_instance = new PrivateBookIdManager());

        public PrivateBookIdManager() : base("PrivateBookIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }

    }
}
