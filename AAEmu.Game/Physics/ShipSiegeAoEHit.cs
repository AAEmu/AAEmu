#nullable enable

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Physics.Util;
using AAEmu.Game.Utils;
using Jitter2.LinearMath;

namespace AAEmu.Game.Physics;

/// <summary>
/// Siege AoE from a ground/world position uses <see cref="WorldManager.GetAround"/> pivot + <see cref="Unit.ModelSize"/>,
/// which misses long ships when the shell lands on the hull away from the pivot. Adds hostile boats whose
/// <c>ship_models</c> mass OBB (XZ) intersects the siege circle, without duplicating targets already collected.
/// </summary>
public static class ShipSiegeAoEHit
{
    /// <summary>
    /// Extra meters added to siege radius for <see cref="WorldManager.GetAround{T}"/> so pivots outside the skill circle
    /// still pick up long hulls. Tuned below a conservative 200 m: largest playable hulls are ~50 m LOA; mass-box half-length,
    /// beam, and center offset from pivot stay well under ~100 m in normal ship_models data.
    /// </summary>
    public const float PivotQuerySlackMeters = 100f;

    public static void AppendHostileShipsHitBySiegeHullAoE(
        BaseUnit caster,
        SkillTemplate template,
        BaseUnit targetSelf,
        SkillCastTarget? targetCaster,
        List<BaseUnit> possibleTargets)
    {
        if (template.TargetAreaRadius <= 0)
            return;
        if ((DamageType)template.DamageTypeId != DamageType.Siege)
            return;
        if (targetCaster is not (SkillCastPositionTarget or SkillCastPosition2Target or SkillCastPosition3Target))
            return;

        var radius = (float)template.TargetAreaRadius;
        var wx = targetSelf.Transform.World.Position.X;
        var wy = targetSelf.Transform.World.Position.Y;

        var queryR = radius + PivotQuerySlackMeters;
        var nearby = WorldManager.GetAround<Slave>(targetSelf, queryR, false);

        var seen = new HashSet<uint>();
        foreach (var t in possibleTargets)
            seen.Add(t.ObjId);

        foreach (var ship in nearby)
        {
            if (seen.Contains(ship.ObjId))
                continue;
            if (!ship.Template.IsABoat())
                continue;
            if (ship.RigidBody is null || ship.ShipController?.ShipModel is null)
                continue;
            if (!SkillTargetingUtil.IsRelationValid(template.TargetRelation, caster, ship))
                continue;
            if (!TrySiegePointHitsShipMassBoxXz(wx, wy, radius, ship))
                continue;

            possibleTargets.Add(ship);
            seen.Add(ship.ObjId);
        }
    }

    /// <summary>
    /// World horizontal plane matches ship hull math: X with Jitter X, Y with Jitter Z (see <see cref="ShipShipInteraction.GetMassBoxCenterXz"/>).
    /// </summary>
    public static bool TrySiegePointHitsShipMassBoxXz(float worldX, float worldY, float radius, Slave ship)
    {
        var body = ship.RigidBody;
        var model = ship.ShipController?.ShipModel;
        if (body is null || model is null || !ship.Template.IsABoat())
            return false;

        ShipShipInteraction.GetMassBoxCenterXz(body, model, ship.Scale, out var cx, out var cz);
        var rpy = PhysicsUtil.GetYawPitchRollFromMatrix(JMatrix.CreateFromQuaternion(body.Orientation));
        var bow = rpy.Item1 + MathUtil.HalfPi;
        var halfLen = model.MassBoxSizeY * ship.Scale * 0.5f;
        var halfBeam = model.MassBoxSizeX * ship.Scale * 0.5f;

        var px = worldX;
        var pz = worldY;

        var bx = MathF.Cos(bow);
        var bz = MathF.Sin(bow);
        var sx = -MathF.Sin(bow);
        var sz = MathF.Cos(bow);

        var dx = px - cx;
        var dz = pz - cz;
        var u = dx * bx + dz * bz;
        var v = dx * sx + dz * sz;
        u = Math.Clamp(u, -halfLen, halfLen);
        v = Math.Clamp(v, -halfBeam, halfBeam);
        var qx = cx + u * bx + v * sx;
        var qz = cz + u * bz + v * sz;
        var ddx = px - qx;
        var ddz = pz - qz;
        return ddx * ddx + ddz * ddz <= radius * radius;
    }
}
