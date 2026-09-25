using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Cooldown reduction packet with the protocol's signed reduction fields and three reset flags.
/// </summary>
public class SCSkillCooldownReducePacket(uint bc, uint @type, uint @type2, int percent, int count, int reduce, bool rstc, bool rtsc, bool rtstc) : GamePacket(SCOffsets.SCSkillCooldownReducePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(percent);
        stream.Write(count);
        stream.Write(reduce);
        stream.Write(rstc);
        stream.Write(rtsc);
        stream.Write(rtstc);
        return stream;
    }
}
