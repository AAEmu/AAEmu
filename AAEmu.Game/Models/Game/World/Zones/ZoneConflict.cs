namespace AAEmu.Game.Models.Game.World.Zones
{
    public class ZoneConflict
    {
        private ZoneGroup _owner;

        public ushort ZoneGroupId { get; set; }
        public int[] NumKills { get; }
        public int[] NoKillMin { get; }

        public int ConflictMin { get; set; }
        public int WarMin { get; set; }
        public int PeaceMin { get; set; }

        public uint PeaceProtectedFactionId { get; set; }
        public uint NuiaReturnPointId { get; set; }
        public uint HariharaReturnPointId { get; set; }
        public uint WarTowerDefId { get; set; }
        // TODO 1.2 // public uint PeaceTowerDefId { get; set; }

        public ZoneConflict(ZoneGroup owner)
        {
            _owner = owner;

            NumKills = new int[5];
            NoKillMin = new int[5];
        }
    }
}