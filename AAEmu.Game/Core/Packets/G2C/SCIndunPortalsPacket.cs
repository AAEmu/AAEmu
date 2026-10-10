using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Indun;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The instance portals the character can see. Sent as the world-entry burst opens, next to the visit
/// counts and the battlefield records: the client populates its instance window from this list and
/// never asks for it.
/// </summary>
/// <remarks>
/// Wire: u32 count, then <c>count</c> rows of u32 indunZoneKey, u32 portalZoneKey, f32 x, f32 y, f32 z.
/// The row carries the portal's own position in the world the character is standing in, which is what
/// the window points at.
/// </remarks>
public class SCIndunPortalsPacket(IReadOnlyList<IndunPortalPoint> portals)
    : GamePacket(SCOffsets.SCIndunPortalsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var rows = portals ?? [];
        stream.Write(rows.Count);
        foreach (var row in rows)
        {
            stream.Write(row.IndunZoneKey);
            stream.Write(row.PortalZoneKey);
            stream.Write(row.X);
            stream.Write(row.Y);
            stream.Write(row.Z);
        }

        return stream;
    }
}
