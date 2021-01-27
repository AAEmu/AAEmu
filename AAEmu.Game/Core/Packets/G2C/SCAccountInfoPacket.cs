using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCAccountInfoPacket : GamePacket
    {
        private readonly int _payMethod;
        private readonly int _payLocation;
        private readonly DateTime _payStart;
        private readonly DateTime _payEnd;

        public SCAccountInfoPacket(int payMethod, int payLocation, DateTime payStart, DateTime payEnd)
            : base(SCOffsets.SCAccountInfoPacket, 5)
        {
            _payMethod = payMethod;
            _payLocation = payLocation;
            _payStart = payStart;
            _payEnd = payEnd;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_payMethod);
            stream.Write(_payLocation);
            stream.Write(_payStart);
            stream.Write(_payEnd);
            stream.Write((long)0); // realPayTime
            return stream;
        }
    }
}
