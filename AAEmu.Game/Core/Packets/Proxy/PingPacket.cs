using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class PingPacket : GamePacket
    {
        public PingPacket() : base(PPOffsets.PingPacket, 2)
        {
        }

        public override void Read(PacketStream stream)
        {
            var tm = stream.ReadInt64(); // tPhy
            var when = stream.ReadInt64(); // ping
            var local = stream.ReadUInt32();

            Connection.LastPing = DateTime.UtcNow;
            Connection.SendPacket(new PongPacket(tm, when, local));
        }
    }
}
