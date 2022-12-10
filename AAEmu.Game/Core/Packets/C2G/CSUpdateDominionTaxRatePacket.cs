using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSUpdateDominionTaxRatePacket : GamePacket
    {
        public CSUpdateDominionTaxRatePacket() : base(CSOffsets.CSUpdateDominionTaxRatePacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt16();
            var taxRate = stream.ReadInt32();
            
            _log.Debug("UpdateDominionTaxRate, Id: {0}, TaxRate: {1}", id, taxRate);
        }
    }
}
