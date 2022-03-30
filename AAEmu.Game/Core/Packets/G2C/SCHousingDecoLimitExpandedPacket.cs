using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHousingDecoLimitExpandedPacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly int _count;
        
        public SCHousingDecoLimitExpandedPacket(ushort tl, int count) : base(SCOffsets.SCHousingDecoLimitExpandedPacket, 5)
        {
            _tl = tl;
            _count = count;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);    // tl (houseId)
            stream.Write(_count); // count
            return stream;
        }
    }
}
