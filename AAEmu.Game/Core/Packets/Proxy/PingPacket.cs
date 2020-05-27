using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class PingPacket : GamePacket
    {
        public PingPacket() : base(0x012, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tm = stream.ReadInt64(); // tPhy
            var when = stream.ReadInt64(); // ping
            var local = stream.ReadUInt32();

            DbLoggerCategory.Database.Connection.SendPacket(new PongPacket(tm, when, local));
        }
    }
}