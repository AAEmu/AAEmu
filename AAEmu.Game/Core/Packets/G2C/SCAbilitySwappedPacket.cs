using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCAbilitySwappedPacket(uint objId, AbilityType oldAbilityId, AbilityType abilityId)
    : GamePacket(SCOffsets.SCAbilitySwappedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write((byte)oldAbilityId);
        stream.Write((byte)abilityId);
        return stream;
    }
}
