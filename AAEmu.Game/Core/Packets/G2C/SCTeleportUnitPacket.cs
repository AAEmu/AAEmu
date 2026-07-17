using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Teleport;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTeleportUnitPacket(TeleportReason reason, short errorMessage, float x, float y, float z, float z2)
    : GamePacket(SCOffsets.SCTeleportUnitPacket, 5)
{
    private readonly byte _reason = (byte)reason;

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_reason);
        stream.Write(errorMessage);
        stream.WritePosition(x, y, z);
        stream.Write(z2);
        return stream;
    }
}
