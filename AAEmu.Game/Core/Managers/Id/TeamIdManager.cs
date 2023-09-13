using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class TeamIdManager : IdManager
    {
        private static TeamIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0x00FFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = { { } };

        public static TeamIdManager Instance => _instance ?? (_instance = new TeamIdManager());

        public TeamIdManager() : base("TeamIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
