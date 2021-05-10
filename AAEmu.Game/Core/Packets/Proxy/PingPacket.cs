using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Tasks.Ping;

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

            Connection.SendPacket(new PongPacket(tm, when, local));

            //// TODO we will make a delay of 10 msec to check the disconnects
            //var pingTask = new PingTickTask(Connection, tm, when, local);
            //TaskManager.Instance.Schedule(pingTask, TimeSpan.FromMilliseconds(10));
        }
    }
}
