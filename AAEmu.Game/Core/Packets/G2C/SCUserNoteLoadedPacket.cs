using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCUserNoteLoadedPacket(uint noteId, bool isTooltip, sbyte invenType, uint noteLen, string title, string notes) : GamePacket(SCOffsets.SCUserNoteLoadedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(noteId);
        stream.Write(isTooltip);
        stream.Write(invenType);
        stream.Write(noteLen);
        stream.Write(title);
        stream.Write(notes);
        return stream;
    }
}
