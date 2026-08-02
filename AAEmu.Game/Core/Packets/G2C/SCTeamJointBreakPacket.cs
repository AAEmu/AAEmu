using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCTeamJointBreakPacket(bool ask, bool accept) : GamePacket(SCOffsets.SCTeamJointBreakPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(ask);
        stream.Write(accept);
        return stream;
    }
}
