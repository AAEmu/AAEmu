using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Drops a player into a match that is already being played, which is how a dungeon hands over: it
/// has no opening ceremony to sit through, so its players skip the standby, ready and countdown
/// screens a battle field runs and are simply in and playing.
/// </summary>
/// <remarks>
/// The client resolves <paramref name="instanceId"/> against its own instance catalogue and takes
/// the dungeon or battle field shape from that record, so this is also what teaches it which of the
/// two it is in. Field order, widths and names come from the 10.0.2.13 client's serializer: zi,
/// type, type, serverStart, count, then per roster entry worldId, type and name.
/// </remarks>
public class SCInstantGameReentryPacket(
    ZoneInstanceId zoneInstanceId,
    uint instanceId,
    uint type,
    long serverStart,
    IReadOnlyList<InstantGameRosterMember> roster = null)
    : GamePacket(SCOffsets.SCInstantGameReentryPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(instanceId);
        stream.Write(type);
        stream.Write(serverStart);
        return InstantGameRosterWire.Write(stream, roster);
    }
}
