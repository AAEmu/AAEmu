using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S
{
    public class CTEmblemPartDownloadedPacket : StreamPacket
    {
        public CTEmblemPartDownloadedPacket() : base(CTOffsets.CTEmblemPartDownloadedPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var previousIndex = stream.ReadInt32();
            var previousSize = stream.ReadInt32(); // TODO: Verify if this size matches maybe ?

            UccManager.Instance.RequestUccPart(Connection, previousIndex, previousSize);
        }
    }
}
