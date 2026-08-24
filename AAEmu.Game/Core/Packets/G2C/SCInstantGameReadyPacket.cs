using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.InstantGame;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The battle field everyone just joined is ready to play. Its players sit on a standby screen from
/// the moment they join until this arrives, and cannot leave, so a battle field that never reports
/// itself ready traps them.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: zi, type, now, count, then per roster entry worldId, type and
/// name.
/// </remarks>
public class SCInstantGameReadyPacket(
    ZoneInstanceId zoneInstanceId,
    uint type,
    long now,
    IReadOnlyList<InstantGameRosterMember> roster = null)
    : GamePacket(SCOffsets.SCInstantGameReadyPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(type);
        stream.Write(now);
        return InstantGameRosterWire.Write(stream, roster);
    }
}
