using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTEmblemStreamDownloadStatusPacket : StreamPacket
    {
        public CTEmblemStreamDownloadStatusPacket() : base(CTOffsets.CTEmblemStreamDownloadStatusPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var type = stream.ReadUInt64();
            var status = stream.ReadByte();
            var count = stream.ReadInt32();
            
            UccManager.Instance.DownloadStatus(Connection, type, status, count);
        }
    }
}
