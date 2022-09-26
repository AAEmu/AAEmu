using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSwapAbilityPacket : GamePacket
    {
        public CSSwapAbilityPacket() : base(CSOffsets.CSSwapAbilityPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();           // bc
            var oldAbilityId = stream.ReadByte();  // o
            var abilityId = stream.ReadByte();     // n
            var auap = stream.ReadBoolean();       // auap
            
            Connection.ActiveChar.Abilities.Swap((AbilityType)oldAbilityId, (AbilityType)abilityId);
        }
    }
}
