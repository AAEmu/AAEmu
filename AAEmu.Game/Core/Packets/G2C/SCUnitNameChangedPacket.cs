using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitNameChangedPacket(uint objId, string name) : GamePacket(SCOffsets.SCUnitNameChangedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(name);
        return stream;
    }
}
