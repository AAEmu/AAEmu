using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCDominionTaxRatePacket : GamePacket
    {
        private readonly ushort _id;
        private readonly int _taxRate;

        public SCDominionTaxRatePacket(ushort id, int taxRate) : base(SCOffsets.SCDominionTaxRatePacket, 5)
        {
            _id = id;
            _taxRate = taxRate;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_id);
            stream.Write(_taxRate);
            return stream;
        }
    }
}
