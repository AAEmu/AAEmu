namespace AAEmu.Game.Models.Game.TowerDefs;

public class TowerDef
{
    public uint Id { get; set; }
    public string Name { get; set; }
    public string StartMsg { get; set; }
    public string EndMsg { get; set; }
    public string TitleMsg { get; set; }
    public float TimeOfDay { get; set; }
    public float FirstWaveAfter { get; set; }
    public uint TargetNpcSpawnId { get; set; }
    public uint KillNpcId { get; set; }
    public uint KillNpcCount { get; set; }
    public float ForceEndTime { get; set; }
    public uint TimeOfDayDayInterval { get; set; }
    public uint MilestoneId { get; set; }

    public List<TowerDefProg> Progs;
}
