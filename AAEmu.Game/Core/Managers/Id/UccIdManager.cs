using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class UccIdManager : IdManager
    {
        private static UccIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"uccs", "id"}};
        
        public static UccIdManager Instance => _instance ?? (_instance = new UccIdManager());
        
        public UccIdManager() : base("UccIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
