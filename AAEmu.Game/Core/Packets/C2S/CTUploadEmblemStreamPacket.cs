using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;
using AAEmu.Game.Models.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTUploadEmblemStreamPacket : StreamPacket
    {
        public CTUploadEmblemStreamPacket() : base(0x0C)
        {
        }

        public override void Read(PacketStream stream)
        {
            var total = stream.ReadInt32();
            var size = stream.ReadInt32();
            var index = stream.ReadUInt32();
            var data = stream.ReadBytes(size); // or bytes; max length 3096
            
            var uccPart = new UccPart()
            {
                Total = total,
                Size = size,
                Index = index,
                Data = data
            };
            
            UccManager.Instance.UploadPart(Connection, uccPart);
        }
    }
}
