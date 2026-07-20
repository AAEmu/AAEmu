using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCForceAttackSetPacket(uint objId, bool on) : GamePacket(SCOffsets.SCForceAttackSetPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(on);
        return stream;
    }
}
