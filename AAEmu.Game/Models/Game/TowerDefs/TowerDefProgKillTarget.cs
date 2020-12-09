namespace AAEmu.Game.Models.Game.TowerDefs
{
    public class TowerDefProgKillTarget
    {
        public uint Id { get; set; }
        public TowerDefProg TowerDefProg { get; set; }
        public uint KillTargetId { get; set; }
        public string KillTargetType { get; set; }
        public uint KillCount { get; set; }
    }
}
