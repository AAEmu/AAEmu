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
public class SCFamilyInfoSetPacket(int familyId, uint level, uint exp, string name, int @type, uint incMemberCount, long changeNameTime) : GamePacket(SCOffsets.SCFamilyInfoSetPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(familyId);
        stream.Write(level);
        stream.Write(exp);
        stream.Write(name);
        stream.Write(@type);
        stream.Write(incMemberCount);
        stream.Write(changeNameTime);
        return stream;
    }
}
