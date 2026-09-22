using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTEmblemStreamUploadStatusPacket() : StreamPacket(CTOffsets.CTEmblemStreamUploadStatusPacket)
{
    public override void Read(PacketStream stream)
    {
        var status = stream.ReadByte();

        // Zero means every part went out and the upload is finalized (and grants); any other value is a
        // failed upload that is acknowledged and discarded without granting or charging anything.
        UccManager.Instance.HandleUploadStatus(Connection, status);
    }
}
