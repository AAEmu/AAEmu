#nullable enable

using System.Numerics;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Utils;

/// <summary>
/// Siege AoE from a ground/world position uses <see cref="WorldManager.GetAround"/> pivot + <see cref="Unit.ModelSize"/>,
/// which misses long ships when the shell lands on the hull away from the pivot. Adds hostile boats whose
/// <c>ship_models</c> mass OBB (XY) intersects the siege circle, without duplicating targets already collected.
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
            if (!SkillTargetingUtil.IsRelationValid(template.TargetRelation, caster, ship))
                continue;
            if (!TrySiegePointHitsShipMassBoxXy(wx, wy, radius, ship))
                continue;

            possibleTargets.Add(ship);
            seen.Add(ship.ObjId);
        }
    }

    /// <summary>
    /// Nearest-point test against the hull's <c>ship_models</c> mass box in the world horizontal plane.
    /// Box axes come from the slave transform: bow is yaw + 90°, matching the forward = (-sin, cos) convention
    /// the rest of the vehicle code uses, with length on <c>mass_box_size_y</c> and beam on <c>mass_box_size_x</c>.
    /// </summary>
    public static bool TrySiegePointHitsShipMassBoxXy(float worldX, float worldY, float radius, Slave ship)
    {
        if (!ship.Template.IsABoat())
            return false;
        if (ModelManager.Instance.GetShipModel(ship.ModelId) is not ShipModelV1 model)
            return false;

        GetMassBoxCenterXy(ship, model, out var cx, out var cy);
        var bow = ship.Transform.World.Rotation.Z + MathUtil.HalfPi;
        var halfLen = model.MassBoxSizeY * ship.Scale * 0.5f;
        var halfBeam = model.MassBoxSizeX * ship.Scale * 0.5f;

        var bx = MathF.Cos(bow);
        var by = MathF.Sin(bow);
        var sx = -MathF.Sin(bow);
        var sy = MathF.Cos(bow);

        var dx = worldX - cx;
        var dy = worldY - cy;
        var u = Math.Clamp(dx * bx + dy * by, -halfLen, halfLen);
        var v = Math.Clamp(dx * sx + dy * sy, -halfBeam, halfBeam);
        var qx = cx + u * bx + v * sx;
        var qy = cy + u * by + v * sy;
        var ddx = worldX - qx;
        var ddy = worldY - qy;
        return ddx * ddx + ddy * ddy <= radius * radius;
    }

    /// <summary>World XY of the hull's mass-box center: the model's local mass center oriented by the slave transform.</summary>
    private static void GetMassBoxCenterXy(Slave ship, ShipModelV1 model, out float cx, out float cy)
    {
        var local = new Vector3(model.MassCenterX, model.MassCenterY, model.MassCenterZ) * ship.Scale;
        var world = Vector3.Transform(local, ship.Transform.World.ToQuaternion()) + ship.Transform.World.Position;
        cx = world.X;
        cy = world.Y;
    }
}
