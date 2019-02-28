using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class MateIdManager : IdManager
    {
        private static MateIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { "mates", "id" } };

        public static MateIdManager Instance => _instance ?? (_instance = new MateIdManager());

        public MateIdManager() : base("MateIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
