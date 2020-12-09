using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.TowerDefs
{
    public class TowerDef
    {
        public uint Id { get; set; }
        // StartMsg
        // EndMsg
        public float TimeOfDay { get; set; }
        public float FirstWaveAfter { get; set; }
        public uint TargetNpcSpawnId { get; set; }
        public uint KillNpcId { get; set; }
        public uint KillNpcCount { get; set; }
        public float ForceEndTime { get; set; }
        public uint TimeOfDayDayInterval { get; set; }
        // TitleMsg
        // MilestoneId

        public List<TowerDefProg> Progs;
    }
}
