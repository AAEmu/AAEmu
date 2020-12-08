namespace AAEmu.Game.Models.Game.TowerDefs
{
    public class TowerDefProgSpawnTarget
    {
        public uint Id { get; set; }
        public TowerDefProg TowerDefProg { get; set; }
        public uint SpawnTargetId { get; set; }
        public string SpawnTargetType { get; set; }
        public bool DespawnOnNextStep { get; set; }
    }
}
