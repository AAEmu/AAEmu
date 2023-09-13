using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class TlIdManager : IdManager
    {
        private static TlIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x0000FFFE;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { } };

        public static TlIdManager Instance => _instance ?? (_instance = new TlIdManager());

        public TlIdManager() : base("TlIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}