using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCSkillCooldownReducePacket(uint bc, int @type, int @type2, uint percent, uint count, uint reduce, bool rstc, bool rtsc, bool rtstc) : GamePacket(SCOffsets.SCSkillCooldownReducePacket, 1)
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
