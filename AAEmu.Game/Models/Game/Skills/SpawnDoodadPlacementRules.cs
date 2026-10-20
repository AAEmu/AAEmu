using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Where a plot-spawned doodad stands. The plot has already resolved the effect target:
/// a location (the area stand) is that point, facing included. A doodad whose effect
/// target is the caster itself is stepped one metre forward so it does not spawn inside
/// the body.
/// </summary>
public static class SpawnDoodadPlacementRules
{
    public const float CasterClearanceMetres = 1f;

    public static BaseUnit Anchor(BaseUnit caster, BaseUnit target) => target ?? caster;

    public static bool NudgeOffCaster(BaseUnit caster, BaseUnit target) =>
        caster != null && (target == null || target.ObjId == caster.ObjId);
}
