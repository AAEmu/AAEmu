using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDominionTaxBalancedPacket : GamePacket
    {
        private readonly ushort _id;
        private readonly int _peaceTaxMoney;
        private readonly int _peaceTaxAaPoint;

        public SCDominionTaxBalancedPacket(ushort id, int peaceTaxMoney, int peaceTaxAaPoint) : base(SCOffsets.SCDominionTaxBalancedPacket, 5)
        {
            _id = id;
            _peaceTaxMoney = peaceTaxMoney;
            _peaceTaxAaPoint = peaceTaxAaPoint;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_peaceTaxMoney);
            stream.Write(_peaceTaxAaPoint);
            return stream;
        }
    }
}
