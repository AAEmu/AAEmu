using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class NpcSpawnerIdManager : IdManager
    {
        private static NpcSpawnerIdManager _instance;
        private const uint FirstId = 0x00000001;
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{ "npc_spawners", "id"}};

        public static NpcSpawnerIdManager Instance => _instance ?? (_instance = new NpcSpawnerIdManager());

        public NpcSpawnerIdManager() : base(nameof(NpcSpawnerIdManager), FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
