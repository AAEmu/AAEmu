using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Charge-cooldown change packet. The client serializer writes type through its i32 slot and
/// percent/count/reduce through its u32 slot; the C# int values preserve authored negative content
/// while producing the same four-byte wire values.
/// </summary>
public class SCChargeSkillCooldownChangedPacket(uint bc, uint @type, int percent, int count, int reduce) : GamePacket(SCOffsets.SCChargeSkillCooldownChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(@type);
        stream.Write(percent);
        stream.Write(count);
        stream.Write(reduce);
        return stream;
    }
}
