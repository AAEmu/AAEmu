using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Butlers;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Opcode 0x345. The 10.0.2.13 client's Butler state serializer writes the house name
/// before the complete nested Butler state.
/// </summary>
public class SCButlerInitInfoPacket(string houseName, ButlerInfoWire info)
    : GamePacket(SCOffsets.SCButlerInitInfoPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(houseName);
        info.Write(stream);
        return stream;
    }
}
