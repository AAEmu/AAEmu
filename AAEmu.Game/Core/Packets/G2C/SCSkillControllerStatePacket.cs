using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillControllerStatePacket(uint objId, byte scType, float len, bool teared, bool cutouted)
    : GamePacket(SCOffsets.SCSkillControllerStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(scType);
        stream.Write(len);
        stream.Write(teared);
        stream.Write(cutouted);
        return stream;
    }
}
