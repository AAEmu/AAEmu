using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Charge-cooldown change packet with signed reduction fields on the wire.
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
