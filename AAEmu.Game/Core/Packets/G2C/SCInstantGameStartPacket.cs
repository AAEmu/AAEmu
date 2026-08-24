using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The match is now being played. This is what clears the client's standby state and lets its
/// players act on the match, leaving included.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: zi, now, curRound.
/// </remarks>
public class SCInstantGameStartPacket(ZoneInstanceId zoneInstanceId, long now, uint curRound)
    : GamePacket(SCOffsets.SCInstantGameStartPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(now);
        stream.Write(curRound);
        return stream;
    }
}
