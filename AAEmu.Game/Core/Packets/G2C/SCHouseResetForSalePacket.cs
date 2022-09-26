using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCHouseResetForSalePacket : GamePacket
    {
        private readonly ushort _tl;
        private readonly string _houseName;
        
        public SCHouseResetForSalePacket(ushort tl, string houseName) : base(SCOffsets.SCHouseResetForSalePacket, 5)
        {
            _tl = tl;
            _houseName = houseName;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_tl);
            stream.Write(_houseName);
            return stream;
        }
    }
}
