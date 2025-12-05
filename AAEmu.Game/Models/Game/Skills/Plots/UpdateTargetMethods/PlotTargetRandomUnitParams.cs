using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;

public class PlotTargetRandomUnitParams(PlotEventTemplate template) : IPlotTargetParams
{
    public AreaShape Shape { get; set; } = WorldManager.Instance.GetAreaShapeById((uint)template.TargetUpdateMethodParam1); // TODO: Change to AreaShape object
    public bool HitOnce { get; set; } = template.TargetUpdateMethodParam2 == 1;
    public SkillTargetRelation UnitRelationType { get; set; } = (SkillTargetRelation)template.TargetUpdateMethodParam3; // TODO: Change to enum
    public byte UnitTypeFlag { get; set; } = (byte)template.TargetUpdateMethodParam4;
}
