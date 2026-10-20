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

        var (destX, destY) = MoveToGroundRules.DestinationXy(unit, target, skill?.CastAim);
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
        // Marker teleports (plot Area / RandomArea location units) must still land when the
        // heightmap sample misses — refusing left every plot_only move_to_ground as a no-op after
        // GCD/mana were spent. In-place snaps without a marker keep the old refuse (no floor →
        // do nothing).
        if (!GroundSnapRules.TryGetFloorZ(rawZ, ground, overWater, waterSurfaceZ, out var z))
        {
            if (target is not { ObjId: uint.MaxValue })
                return;
            z = rawZ;
            Logger.Warn(
                "MoveToGround heightmap miss for {0} at ({1:F1},{2:F1}); keeping marker Z={3:F1}",
                unit.ObjId, destX, destY, z);
        }

        Logger.Debug(
            "MoveToGround unit={0} dest=({1:F1},{2:F1},{3:F1}) targetObj={4} castAim={5}",
            unit.ObjId, destX, destY, z,
            target?.ObjId,
            skill?.CastAim is { } aim ? $"({aim.X:F1},{aim.Y:F1})" : "none");
        // Soft blink only — never SCTeleportUnit (that is a load-screen teleport).
        SkillBlinkLanding.Apply(unit, destX, destY, z, move3D: true);
        skill?.NoteCasterSoftMoved();
    }
}
