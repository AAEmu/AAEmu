using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCCurServerTimePacket : GamePacket
    {
        private readonly DateTime _time;

        public SCCurServerTimePacket(DateTime time) : base(SCOffsets.SCCurServerTimePacket, 5)
        {
            _time = time;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_time);
            return stream;
        }
    }
}
