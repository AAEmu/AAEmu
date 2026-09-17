using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client that the sensitive-operation verification it was sent a URL for is done.
/// </summary>
/// <remarks>The 10.0.2.13 client reads no fields for this packet.</remarks>
public class SCSensitiveOperationVerifySuccessPacket()
    : GamePacket(SCOffsets.SCSensitiveOperationVerifySuccessPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        return stream;
    }
}
