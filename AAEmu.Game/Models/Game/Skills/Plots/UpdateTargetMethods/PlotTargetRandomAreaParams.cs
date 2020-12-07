using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods
{
    public class PlotTargetRandomAreaParams : IPlotTargetParams
    {
        public AreaShape Shape { get; set; } // TODO: Change to AreaShape object
        public int MaxTargets { get; set; }
        public int Distance { get; set; }
        public int HeightOffset { get; set; }//This is not confirmed
        public int UnkValue { get; set; }//Possibly Radius?
        public bool HitOnce { get; set; }
        public SkillTargetRelation UnitRelationType { get; set; } // TODO: Change to enum
        public byte UnitTypeFlag { get; set; }


        public PlotTargetRandomAreaParams(PlotEventTemplate template)
        {
            Shape = WorldManager.Instance.GetAreaShapeById((uint)template.TargetUpdateMethodParam1);
            MaxTargets = template.TargetUpdateMethodParam2;
            Distance = template.TargetUpdateMethodParam3;
            HeightOffset = template.TargetUpdateMethodParam4;
            UnkValue = template.TargetUpdateMethodParam5;
            HitOnce = template.TargetUpdateMethodParam6 == 1;
            UnitRelationType = (SkillTargetRelation)template.TargetUpdateMethodParam7;
            UnitTypeFlag = (byte)template.TargetUpdateMethodParam8;
        }
    }
}
