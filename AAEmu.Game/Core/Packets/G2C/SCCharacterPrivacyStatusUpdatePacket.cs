using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterPrivacyStatusUpdatePacket() : GamePacket(SCOffsets.SCCharacterPrivacyStatusUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Reference world-entry body is 2 bytes: privacy visibility level (1) and a reserved/flag byte (0).
        stream.Write((byte)1);
        stream.Write((byte)0);

        return stream;
    }
}
