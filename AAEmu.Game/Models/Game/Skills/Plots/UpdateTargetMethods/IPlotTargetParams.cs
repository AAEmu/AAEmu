using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Plots.UpdateTargetMethods;

internal interface IPlotTargetParams
{
    AreaShape Shape { get; set; } // TODO: Change to AreaShape object
    bool HitOnce { get; set; }
    SkillTargetRelation UnitRelationType { get; set; } // TODO: Change to enum
    byte UnitTypeFlag { get; set; }
}
