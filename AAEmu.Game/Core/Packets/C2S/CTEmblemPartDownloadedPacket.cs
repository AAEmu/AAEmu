using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTEmblemPartDownloadedPacket : StreamPacket
    {
        public CTEmblemPartDownloadedPacket() : base(0x11)
        {
        }

        public override void Read(PacketStream stream)
        {
            var index = stream.ReadInt32();
            var size = stream.ReadInt32();
        }
    }
}