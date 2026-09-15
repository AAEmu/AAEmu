using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Managers.World;

/// <summary>
/// A zone host keeps a mirror of every unit it can see, players included, but it never simulates a
/// player: the mirror is created where the World told the zone the player was standing and nothing
/// moves it again. The zone still streams that mirror in its ZWUnitMovements batches, and the World
/// relays those batches to clients.
/// </summary>
/// <remarks>
/// Measured 2026-09-15 with two clients at the Marianople courthouse: a GM <c>/move</c>d character
/// was streamed by the zone from its pre-move position in every batch (about ten a second, the same
/// bytes each time) while the World fanned the real movement out itself. The observing client
/// alternated between the two - the "other player flickers and is mostly invisible" report.
/// <para>
/// Player movement is client-authored and the World relays it (<c>CSMoveUnitPacket</c> broadcasts
/// every self move to the observers it has), so a zone entry for a player can only ever be stale.
/// </para>
/// </remarks>
public static class ZonePlayerMirrorStreamRules
{
    /// <summary>
    /// True when a zone movement entry must not reach clients. Only a resolved
    /// <see cref="Character"/> is dropped: an id that resolves to nothing at all is a zone-only
    /// mirror (an NPC or hull the zone owns) and the zone's stream is its only source.
    /// </summary>
    public static bool ShouldDropZoneMovementFor(BaseUnit unit) => unit is Character;
}
