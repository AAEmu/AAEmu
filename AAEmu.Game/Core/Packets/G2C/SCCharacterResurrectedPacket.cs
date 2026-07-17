using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterResurrectedPacket(uint unitId, float x, float y, float z, float zRot)
    : GamePacket(SCOffsets.SCCharacterResurrectedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.WritePosition(x, y, z);
        stream.Write(zRot);
        return stream;
    }
}
