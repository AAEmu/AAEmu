using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class MusicIdManager : IdManager
    {
        private static MusicIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"music", "id"}};
        
        public static MusicIdManager Instance => _instance ?? (_instance = new MusicIdManager());
        
        public MusicIdManager() : base("MusicIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
