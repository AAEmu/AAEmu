using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSBuySpecialtyItemPacket : GamePacket
    {
        public CSBuySpecialtyItemPacket() : base(CSOffsets.CSBuySpecialtyItemPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            var id = stream.ReadUInt32();
            var refund = stream.ReadInt32();
            var currency = stream.ReadByte();
            var type = stream.ReadByte();

            var objId = stream.ReadBc();

            _log.Warn("BuySpecialtyItem, Id: {0}, Currency: {1}", id, currency);
        }
    }
}
