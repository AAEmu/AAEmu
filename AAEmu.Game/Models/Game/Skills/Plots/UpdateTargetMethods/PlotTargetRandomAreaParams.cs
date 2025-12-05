using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;

public class PlotTargetRandomAreaParams(PlotEventTemplate template) : IPlotTargetParams
{
    public AreaShape Shape { get; set; } = WorldManager.Instance.GetAreaShapeById((uint)template.TargetUpdateMethodParam1); // TODO: Change to AreaShape object
    public int MaxTargets { get; set; } = template.TargetUpdateMethodParam2;
    public int Distance { get; set; } = template.TargetUpdateMethodParam3;
    public int HeightOffset { get; set; } = template.TargetUpdateMethodParam4; //This is not confirmed
    public int UnkValue { get; set; } = template.TargetUpdateMethodParam5; //Possibly Radius?
    public bool HitOnce { get; set; } = template.TargetUpdateMethodParam6 == 1;
    public SkillTargetRelation UnitRelationType { get; set; } = (SkillTargetRelation)template.TargetUpdateMethodParam7; // TODO: Change to enum
    public byte UnitTypeFlag { get; set; } = (byte)template.TargetUpdateMethodParam8;
}
