using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUploadEmblemStreamPacket : StreamPacket
    {
        public CTUploadEmblemStreamPacket() : base(CTOffsets.CTUploadEmblemStreamPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var total = stream.ReadInt32();
            var size = stream.ReadInt32();
            var index = stream.ReadUInt32();
            var partSize = stream.ReadUInt16();
            var data = stream.ReadBytes(partSize); // or bytes; max length 3096
            
            var uccPart = new UccPart()
            {
                Total = total,
                Size = partSize,
                Index = index,
                Data = data
            };

            _log.Warn("CTUploadEmblemStreamPacket, total:{0}, size:{1}, index:{2}", total, partSize, index);
            
            UccManager.Instance.UploadPart(Connection, uccPart);
        }
    }
}
