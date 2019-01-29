using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTStartUploadEmblemStreamPacket : StreamPacket
    {
        public CTStartUploadEmblemStreamPacket() : base(0x0E)
        {
        }

        public override void Read(PacketStream stream)
        {
            var bc = stream.ReadBc();
            var type = stream.ReadInt64();
            var total = stream.ReadInt32();
            // -----------------------
            var pat1 = stream.ReadInt32();
            var pat2 = stream.ReadInt32();
            for (var i = 0; i < 3; i++)
            {
                var r = stream.ReadInt32();
                var g = stream.ReadInt32();
                var b = stream.ReadInt32();
            }
            // -----------------------
            var modified = stream.ReadUInt64();
        }
    }
}