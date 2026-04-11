using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.Debug;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillControllerStatePacket(uint objId, byte scType, float len, bool teared, bool cutouted)
    : GamePacket(SCOffsets.SCSkillControllerStatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        SkillControllerPacketDebug.LogScSkillControllerState(objId, scType, len, teared, cutouted);
        stream.WriteBc(objId);
        stream.Write(scType);
        stream.Write(len);
        stream.Write(teared);
        stream.Write(cutouted);
        return stream;
    }
}
