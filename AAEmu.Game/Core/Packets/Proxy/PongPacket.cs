using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy
{
    public class PongPacket : GamePacket
    {
        private long _tm;
        private long _when;
        private uint _local;
        private uint _world;

        public PongPacket(long tm, long when, uint local) : base(PPOffsets.PongPacket, 2)
        {
            _tm = tm;
            _when = when;
            _local = local;
            _world = (uint) (Environment.TickCount & int.MaxValue);
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tm);
            stream.Write(_when);
            stream.Write((long) 0); // elapsed
            stream.Write((long) _world * 1000); // world * 1000; remote
            stream.Write(_local);
            stream.Write(_world); // TODO packet sleep 250ms...

            return stream;
        }
    }
}
