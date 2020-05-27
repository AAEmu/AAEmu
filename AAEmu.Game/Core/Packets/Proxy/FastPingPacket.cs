using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Game;
using Microsoft.EntityFrameworkCore;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class FastPingPacket : GamePacket
    {
        public FastPingPacket() : base(0x015, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var sent = stream.ReadUInt32();
            DbLoggerCategory.Database.Connection.SendPacket(new FastPongPacket(sent));
        }
    }
}