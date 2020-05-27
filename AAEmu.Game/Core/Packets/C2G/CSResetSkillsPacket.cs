using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSResetSkillsPacket : GamePacket
    {
        public CSResetSkillsPacket() : base(0x094, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var abilityId = stream.ReadByte();
            var ausp = stream.ReadBoolean();

            DbLoggerCategory.Database.Connection.ActiveChar.Skills.Reset((AbilityType) abilityId);
        }
    }
}