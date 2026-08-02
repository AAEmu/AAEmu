using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSSwapAbilityPacket() : GamePacket(CSOffsets.CSSwapAbilityPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var objId = stream.ReadBc();
        var oldAbilityId = stream.ReadByte();
        var abilityId = stream.ReadByte();
        var auap = stream.ReadBoolean();

        Logger.Info("CSSwapAbility obj={0} old={1} new={2} auap={3}", objId, oldAbilityId, abilityId, auap);
        Connection.ActiveChar.Abilities.Swap((AbilityType)oldAbilityId, (AbilityType)abilityId);
    }
}
