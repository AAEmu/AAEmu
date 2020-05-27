using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSSwapAbilityPacket : GamePacket
    {
        public CSSwapAbilityPacket() : base(0x096, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var objId = stream.ReadBc();
            var oldAbilityId = stream.ReadByte();
            var abilityId = stream.ReadByte();
            var auap = stream.ReadBoolean();
            
            DbLoggerCategory.Database.Connection.ActiveChar.Abilities.Swap((AbilityType)oldAbilityId, (AbilityType)abilityId);
        }
    }
}