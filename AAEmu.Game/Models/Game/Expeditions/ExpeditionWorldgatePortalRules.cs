using System.Numerics;
using AAEmu.Game.Models.Game.Expeditions.Activities;
using Portal = AAEmu.Game.Models.Game.Portal;

namespace AAEmu.Game.Models.Game.Expeditions;

/// <summary>
/// Guild worldgate "open" places a temporary book-style enter/exit portal pair on the
/// worldgate device. NPC templates come from <c>const_npc_types</c> (<c>portal</c> /
/// <c>exit_portal</c>), not from literals.
/// </summary>
public static class ExpeditionWorldgatePortalRules
{
    public const string EnterNpcConstName = "portal";
    public const string ExitNpcConstName = "exit_portal";
    public const string WorldgateDoodadConstName = "expedition_portal";

    public static bool TryResolveNpcPair(uint enterNpcId, uint exitNpcId) =>
        enterNpcId != 0 && exitNpcId != 0;

    public static Portal ToBookPortal(ExpeditionPortalPoint point)
    {
        if (point == null)
            return null;

        return new Portal
        {
            Id = point.Id,
            Name = point.Name ?? string.Empty,
            ZoneId = point.ZoneId,
            X = point.X,
            Y = point.Y,
            Z = point.Z,
            ZRot = point.ZRot,
            Owner = point.ExpeditionId
        };
    }

    /// <summary>
    /// Enter gate sits on the worldgate device at the prefab aimpoint (same local point the
    /// activated <c>portal_on</c> particle uses). The doodad origin alone is the pad base and
    /// buries the unit; book <c>open_portal</c> cast-distance is not a pad stand-off.
    /// </summary>
    public static Vector3 ResolveEnterPosition(
        Vector3? worldgatePosition,
        Vector3 casterPosition,
        Vector3 aimpointLocalOffset = default)
    {
        if (worldgatePosition is null)
            return casterPosition;

        var gate = worldgatePosition.Value;
        var hasAimpoint = aimpointLocalOffset != Vector3.Zero;
        var z = hasAimpoint
            ? gate.Z + aimpointLocalOffset.Z
            : MathF.Max(gate.Z, casterPosition.Z);
        // Never place below the opener standing at the device.
        z = MathF.Max(z, casterPosition.Z);
        return new Vector3(
            gate.X + aimpointLocalOffset.X,
            gate.Y + aimpointLocalOffset.Y,
            z);
    }
}
