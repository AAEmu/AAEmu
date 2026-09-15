using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// The notes of a score item, sent in answer to <see cref="C2G.CSRequestMusicNotesPacket"/>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: the requested score item id, the tooltip flag and the
/// container of the request echoed back, then the score length, the title and the score text.
/// The score text carries its null terminator, which is why the client reads it with a limit of
/// one byte more than <c>noteLen</c>.
/// </remarks>
public class SCUserNoteLoadedPacket : GamePacket
{
    private readonly uint _noteId;
    private readonly bool _isTooltip;
    private readonly sbyte _invenType;
    private readonly string _title;
    private readonly string _notes;

    public SCUserNoteLoadedPacket(uint noteId, bool isTooltip, sbyte invenType, string title, string notes)
        : base(SCOffsets.SCUserNoteLoadedPacket, 1)
    {
        _noteId = noteId;
        _isTooltip = isTooltip;
        _invenType = invenType;
        // The client reads the title into a fixed 96 byte buffer, so a title that does not fit
        // would desync the rest of the packet.
        _title = MusicNoteRules.ClampToBytes(title, MusicNoteRules.MaxTitleBytes);
        _notes = notes ?? string.Empty;
    }

    /// <summary>Length of the score text, without its null terminator.</summary>
    public uint NoteLength => (uint)Encoding.UTF8.GetByteCount(_notes);

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_noteId);
        stream.Write(_isTooltip);
        stream.Write(_invenType);
        stream.Write(NoteLength);
        stream.Write(_title);
        stream.Write(_notes, true, true);
        return stream;
    }
}
