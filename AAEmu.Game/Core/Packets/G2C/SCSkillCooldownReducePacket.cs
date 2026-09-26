using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Cooldown reduction packet. The client serializer writes type/type2 through its i32 slot and
/// percent/count/reduce through its u32 slot; the C# int values preserve authored negative content
/// while producing the same four-byte wire values.
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
