using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Moves a unit onto the terrain floor. Plot teleport / ambush events name a location marker as the
/// target; the unit being moved is the effect source. Without a marker the unit stays on its XY and
/// only Z is snapped (the client already moved, or the cast was not position-targeted).
/// </summary>
public class MoveToGround : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.MoveToGround;

    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        if (caster is not Unit unit || unit.ParentWorld == null)
            return;

        var (destX, destY) = MoveToGroundRules.DestinationXy(unit, target);
        var ground = TerrainFloor.SampleHeightmap(unit.ParentWorld, destX, destY);
        if (ground <= 0f)
            ground = TerrainFloor.SampleHeightmap(unit.Transform.ZoneId, destX, destY);

        var probe = unit.Transform.World.Position;
        var overWater = TerrainFloor.TryWaterSurface(unit.ParentWorld,
            new System.Numerics.Vector3(destX, destY, probe.Z), out var waterSurfaceZ);
        // Snap against the destination XY; when the marker is above the floor the raw Z is the marker's,
        // otherwise the unit's current Z (in-place snap).
        var rawZ = target is { ObjId: uint.MaxValue, Transform: not null }
            ? target.Transform.World.Position.Z
            : probe.Z;
        if (!GroundSnapRules.TryGetFloorZ(rawZ, ground, overWater, waterSurfaceZ, out var z))
            return;

        SkillBlinkLanding.Apply(unit, destX, destY, z, move3D: true);
    }
}
