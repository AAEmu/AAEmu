namespace AAEmu.Game.Models.Game.NpcSpawner
{
    public class NpcSpawnerNpc
    {
        public uint Id { get; set; }
        public uint NpcSpawnerId { get; set; }
        public uint MemberId { get; set; }
        public string MemberType { get; set; }
        public float Weight { get; set; }
    }
}
