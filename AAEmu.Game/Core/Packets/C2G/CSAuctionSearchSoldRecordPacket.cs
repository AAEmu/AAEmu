using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSAuctionSearchSoldRecordPacket : GamePacket
    {
        public CSAuctionSearchSoldRecordPacket() : base(CSOffsets.CSAuctionSearchSoldRecordPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            var unk1 = stream.ReadUInt32();
            var unk2 = stream.ReadByte();
            _log.Debug("CSAuctionSearchSoldRecordPacket");
        }
    }
}
