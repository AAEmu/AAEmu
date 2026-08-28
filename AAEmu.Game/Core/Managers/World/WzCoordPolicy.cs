using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// Retail WZ space is the same rule in every zone. Per-zone origin comes from that zone's
/// world.xml <c>originX/originY</c> (metres = cell × 1024). World never hardcodes a ZoneId.
///
/// Live objects on WZ (player, ZWSpawnNpc remirror, slave/boat, house, doodad, gimmick, CSMove)
/// stay <b>continent</b> — the same numbers as Transform/SC. The dedicate subtracts origin for
/// sectors and ship physics. Rewriting those bodies to zone-local in World makes invalid sector
/// cells and "end of world" hulls in every zone that has a non-zero origin.
///
/// Packets that compare against level files (<c>npc_spawners.g</c> activate circle) convert
/// continent → local once via <see cref="ZoneManager.ConvertToLocalCoordinates"/>.
///
/// World-authored event NPCs (tower/plot army) use the same continent WZ Create as the player.
/// Zone-local Create made dedicate subtract origin twice → invalid sectors and fall-through.
/// </summary>
public static class WzCoordPolicy
{
    /// <summary>Opt-in debug rewrite of live WZ to zone-local. Not retail. Never use for boats.</summary>
    public static bool DebugRewriteLiveToZoneLocal => ZoneCoordBoundary.UseLocalOnZoneWire;

    public static bool KeepContinentOnLiveWz(Unit unit)
    {
        if (unit is Slave { Template: not null } slave && slave.Template.IsABoat())
            return true;
        if (unit is Npc { ZoneSimUsesLocalCoordinates: true })
            return false;
        return !DebugRewriteLiveToZoneLocal;
    }

    /// <summary>
    /// A seated rider (or equipment child) stores the seat in <c>Transform.Local</c>. Live WZ
    /// Create needs the continent point; writing Local places the unit at the seat offset.
    /// </summary>
    public static bool UseWorldPositionOnWz(Unit unit) =>
        unit?.Transform?.Parent != null && KeepContinentOnLiveWz(unit);
}
