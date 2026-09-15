using System.Text;

using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Models.Game.Music;

/// <summary>
/// Decisions about the notes a player writes on a score item: what may be uploaded from the
/// composition window, and which item a "load my notes" request may be answered for.
/// </summary>
/// <remarks>
/// The composition window composes into fixed buffers, so every limit here is counted in UTF-8
/// bytes rather than characters: the title buffer takes 96 bytes and the score buffer 20000,
/// while the shipped composition steps in <c>music_note_limits</c> stop at 5000.
/// </remarks>
public static class MusicNoteRules
{
    /// <summary>Title buffer of the composition window.</summary>
    public const int MaxTitleBytes = 96;

    /// <summary>Highest step of the shipped <c>music_note_limits</c> table.</summary>
    public const int DefaultMaxNoteBytes = 5000;

    /// <summary>Score buffer of the composition window; the ceiling the client itself clamps to.</summary>
    public const int MaxBufferedNoteBytes = 20000;

    /// <summary>Number of UTF-8 bytes a value occupies, or zero when it is null or empty.</summary>
    public static int ByteLength(string value)
    {
        return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
    }

    /// <summary>
    /// Checks a title/score pair that the composition window wants to save before it is queued
    /// for the score item it is going to be written on.
    /// </summary>
    public static bool IsUploadable(string title, string note, int maxNoteBytes, out string reason)
    {
        if (ByteLength(title) == 0)
        {
            reason = "the title is empty";
            return false;
        }

        if (ByteLength(title) > MaxTitleBytes)
        {
            reason = $"the title is longer than {MaxTitleBytes} bytes";
            return false;
        }

        if (ByteLength(note) == 0)
        {
            reason = "the score is empty";
            return false;
        }

        var limit = maxNoteBytes > 0 ? Math.Min(maxNoteBytes, MaxBufferedNoteBytes) : DefaultMaxNoteBytes;
        if (ByteLength(note) > limit)
        {
            reason = $"the score is longer than {limit} bytes";
            return false;
        }

        reason = null;
        return true;
    }

    /// <summary>
    /// Resolves the note a "load notes" request is about. The request names the score item the
    /// client is showing, not the song, so the requester has to own it and it has to be a written
    /// score; notes are never served for somebody else's item.
    /// </summary>
    public static bool TryGetNoteSong(Item scoreItem, uint requesterId, out uint songId)
    {
        songId = 0;
        if (scoreItem == null || scoreItem.OwnerId != requesterId)
            return false;
        if (scoreItem is not MusicSheetItem sheet || sheet.SongId == 0)
            return false;

        songId = sheet.SongId;
        return true;
    }

    /// <summary>
    /// Whether the player holds a written score carrying this song. The composition window names
    /// the item it is composing on, while reading a written score names the song the item carries,
    /// so a song id is only answered while a score with that song is actually held; otherwise any
    /// player could read out every stored composition by counting upwards.
    /// </summary>
    public static bool HoldsNote(IEnumerable<Item> heldItems, uint requesterId, uint songId)
    {
        if (heldItems == null || songId == 0)
            return false;

        foreach (var item in heldItems)
        {
            if (item is MusicSheetItem sheet && sheet.SongId == songId && sheet.OwnerId == requesterId)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Truncates a value so that its UTF-8 encoding fits <paramref name="maxBytes"/>, never
    /// splitting a character. Used when a stored title has to fit the client's title buffer.
    /// </summary>
    public static string ClampToBytes(string value, int maxBytes)
    {
        if (string.IsNullOrEmpty(value) || maxBytes <= 0)
            return string.Empty;
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
            return value;

        // Walk whole scalars: cutting a surrogate pair in half would send a replacement character.
        var kept = new StringBuilder(value.Length);
        var used = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > maxBytes)
                break;

            kept.Append(rune);
            used += rune.Utf8SequenceLength;
        }

        return kept.ToString();
    }
}
