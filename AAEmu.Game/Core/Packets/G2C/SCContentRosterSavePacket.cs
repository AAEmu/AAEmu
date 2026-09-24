using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answer to a content-roster save, including the saved row the client shows in its notice.
/// </summary>
/// <remarks>
/// Field order: bool result, u16 ErrorMessage, then the info section
/// bool cached, i64 id, i64 recordTime, string title.
/// </remarks>
public class SCContentRosterSavePacket(
    bool result,
    ErrorMessageType errorMessage,
    long id,
    long recordTime,
    string title)
    : GamePacket(SCOffsets.SCContentRosterSavePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(result);
        stream.Write((ushort)errorMessage);
        stream.Write(false);
        stream.Write(id);
        stream.Write(recordTime);
        stream.Write(title ?? string.Empty);
        return stream;
    }
}
