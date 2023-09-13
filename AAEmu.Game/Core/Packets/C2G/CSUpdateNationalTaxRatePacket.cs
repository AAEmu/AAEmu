using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpdateNationalTaxRatePacket : GamePacket
    {
        public CSUpdateNationalTaxRatePacket() : base(CSOffsets.CSUpdateNationalTaxRatePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt16();
            var taxRate = stream.ReadInt32();

            _log.Debug("UpdateNationalTaxRate, Id: {0}, TaxRate: {1}", id, taxRate);
        }
    }
}
