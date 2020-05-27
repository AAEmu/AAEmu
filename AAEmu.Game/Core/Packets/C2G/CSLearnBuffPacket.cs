using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSLearnBuffPacket : GamePacket
    {
        public CSLearnBuffPacket() : base(0x092, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var buffId = stream.ReadUInt32();
            
            DbLoggerCategory.Database.Connection.ActiveChar.Skills.AddBuff(buffId);
        }
    }
}