using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x348. The 10.0.2.13 client serializer writes only the raw ErrorMessage value.
/// A successful response resets the client's local Butler state.
/// </summary>
public sealed class SCButlerUnboundPacket(ushort errorMessage)
    : GamePacket(SCOffsets.SCButlerUnboundPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(errorMessage);
        return stream;
    }
}
