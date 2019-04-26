using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class FriendIdManager : IdManager
    {
        private static FriendIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "friends", "id" } };

        public static FriendIdManager Instance => _instance ?? (_instance = new FriendIdManager());

        public FriendIdManager() : base("FriendIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }

    }
}
