using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x346. The 10.0.2.13 client serializer at <c>FUN_39C76210</c> writes the complete Butler state,
/// house name, then the raw ErrorMessage value.
/// </summary>
public class SCButlerBoundPacket(ButlerInfoWire info, string houseName, ushort errorMessage)
    : GamePacket(SCOffsets.SCButlerBoundPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        info.Write(stream);
        stream.Write(houseName);
        stream.Write(errorMessage);
        return stream;
    }
}
