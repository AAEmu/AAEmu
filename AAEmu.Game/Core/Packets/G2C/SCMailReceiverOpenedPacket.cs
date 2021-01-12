using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCMailReceiverOpenedPacket : GamePacket
    {
        private readonly long _mainId;
        private readonly DateTime _openDate;

        public SCMailReceiverOpenedPacket(long mainId, DateTime openDate) : base(SCOffsets.SCMailReceiverOpenedPacket, 5)
        {
            _mainId = mainId;
            _openDate = openDate;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_mainId);   // type
            stream.Write(_openDate); // openDate

            return stream;
        }
    }
}
