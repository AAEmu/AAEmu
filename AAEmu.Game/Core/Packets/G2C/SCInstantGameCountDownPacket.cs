using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The match is counting down to its opening bell. Only a battle field counts down; a dungeon goes
/// from ready straight to playing, and its client rejects this.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: zi, now.
/// </remarks>
public class SCInstantGameCountDownPacket(ZoneInstanceId zoneInstanceId, long now)
    : GamePacket(SCOffsets.SCInstantGameCountDownPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneInstanceId);
        stream.Write(now);
        return stream;
    }
}
