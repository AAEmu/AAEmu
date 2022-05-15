using AAEmu.Game.Utils;

namespace AAEmu.Game.Core.Managers.Id
{
    public class ContainerIdManager : IdManager
    {
        private static ContainerIdManager _instance;
        private const uint FirstId = 0x00010000; // random value
        private const uint LastId = 0xFFFFFFFF;
        private static readonly uint[] Exclude = { };
        private static readonly string[,] ObjTables = {{"item_containers", "container_id"}};

        public static ContainerIdManager Instance => _instance ?? (_instance = new ContainerIdManager());

        public ContainerIdManager() : base("ContainerIdManager", FirstId, LastId, ObjTables, Exclude)
        {
        }
    }
}
