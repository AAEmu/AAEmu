using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods
{
    public class PlotTargetAreaParams : IPlotTargetParams
    {
        public AreaShape Shape { get; set; } // TODO: Change to AreaShape object
        public int MaxTargets { get; set; }
        public int Distance { get; set; }
        public int Angle { get; set; }
        public int MinRange { get; set; }
        public int MaxRange { get; set; }
        public bool HitOnce { get; set; }
        public SkillTargetRelation UnitRelationType { get; set; }
        public byte UnitTypeFlag { get; set; }


        public PlotTargetAreaParams(PlotEventTemplate template)
        {
            Shape = WorldManager.Instance.GetAreaShapeById((uint)template.TargetUpdateMethodParam1);
            MaxTargets = template.TargetUpdateMethodParam2;
            Distance = template.TargetUpdateMethodParam3;
            Angle = template.TargetUpdateMethodParam4;
            MinRange = template.TargetUpdateMethodParam5;
            MaxRange = template.TargetUpdateMethodParam6;
            HitOnce = template.TargetUpdateMethodParam7 == 1;
            UnitRelationType = (SkillTargetRelation)template.TargetUpdateMethodParam8;
            UnitTypeFlag = (byte)template.TargetUpdateMethodParam9;
        }
    }
}
