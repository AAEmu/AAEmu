using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCSkillsResetPacket(uint objId, AbilityType ability) : GamePacket(SCOffsets.SCSkillsResetPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write((byte)ability);
        return stream;
    }
}
