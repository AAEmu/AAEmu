using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.TowerDefs
{
    public class TowerDefProg
    {
        public uint Id { get; set; }
        public TowerDef TowerDef { get; set; }
        // I presume this is how long the step lasts
        public float CondToNextTime { get; set; }
        // This probably is to know wether the conditions have been completed by NextTime ?
        public bool CondCompByAnd { get; set; }
        
        public List<TowerDefProgKillTarget> KillTargets { get; set; }
        public List<TowerDefProgSpawnTarget> SpawnTargets { get; set; }
    }
}
