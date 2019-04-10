using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTEmblemStreamUploadStatusPacket : StreamPacket
    {
        public CTEmblemStreamUploadStatusPacket() : base(0x0D)
        {
        }

        public override void Read(PacketStream stream)
        {
            var status = stream.ReadByte();
        }
    }
}