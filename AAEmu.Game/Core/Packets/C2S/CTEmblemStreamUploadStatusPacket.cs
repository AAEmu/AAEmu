using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.Stream;
using AAEmu.Game.Core.Network.Stream;

namespace AAEmu.Game.Core.Packets.C2S;

public class CTEmblemStreamUploadStatusPacket() : StreamPacket(CTOffsets.CTEmblemStreamUploadStatusPacket)
{
    public override void Read(PacketStream stream)
    {
        var status = stream.ReadByte();

        // The status decides what happens next: only a completed upload is finalized (and grants),
        // progress keeps the queued upload open, and every other value is treated as a failed upload
        // that is acknowledged and discarded without granting or charging anything.
        UccManager.Instance.HandleUploadStatus(Connection, status);
    }
}
