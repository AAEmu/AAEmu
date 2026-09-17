using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;

public class PlotTargetAreaParams(PlotEventTemplate template) : IPlotTargetParams
{
    public AreaShape Shape { get; set; } = WorldManager.Instance.GetAreaShapeById((uint)template.TargetUpdateMethodParam1); // TODO: Change to AreaShape object
    public int MaxTargets { get; set; } = template.TargetUpdateMethodParam2;
    public int Distance { get; set; } = template.TargetUpdateMethodParam3;
    public int Angle { get; set; } = template.TargetUpdateMethodParam4;
    public int HeightOffset { get; set; } = template.TargetUpdateMethodParam5;
    public int UnkValue { get; set; } = template.TargetUpdateMethodParam6; // Possibly Radius
    public bool HitOnce { get; set; } = template.TargetUpdateMethodParam7 == 1;
    public SkillTargetRelation UnitRelationType { get; set; } = (SkillTargetRelation)template.TargetUpdateMethodParam8;
    public byte UnitTypeFlag { get; set; } = (byte)template.TargetUpdateMethodParam9;

    /// <summary>
    /// target_update_method_param10 / _param11 on an Area event: 164 rows set 1 and 26 set 30 in param10,
    /// 273 set 30 in param11. Bound but not interpreted — see
    /// <see cref="PlotEventTemplate.TargetUpdateMethodParam10"/>.
    /// </summary>
    public int Param10 { get; set; } = template.TargetUpdateMethodParam10;
    public int Param11 { get; set; } = template.TargetUpdateMethodParam11;
}
