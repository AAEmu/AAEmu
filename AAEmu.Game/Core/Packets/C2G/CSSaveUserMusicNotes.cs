using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The composition window saving notes: the title and the score text the player just wrote.
/// </summary>
/// <remarks>
/// The body is a length-prefixed title followed by the score text, both preceded by the score item
/// the player is composing on and the composition parameters the window passes along.
/// </remarks>
public class CSSaveUserMusicNotesPacket() : GamePacket(CSOffsets.CSSaveUserMusicNotesPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var bodyLen = stream.ReadInt32(); // the score size, without its null terminator
        var itemId = stream.ReadUInt64(); // the score item the notes are being composed on
        var type = stream.ReadByte(); // composition parameter (tempo/rhythm), echoed back by the window
        var index = stream.ReadByte(); // composition parameter (instrument line)
        var title = stream.ReadString();
        var song = stream.ReadString();

        if (MusicManager.Instance.UploadSong(Connection.ActiveChar.Id, title, song, itemId))
        {
            Logger.Debug("Saved music notes, title: {0}, song size: {1}, type: {2}, index: {3}",
                title, bodyLen, type, index);
            Logger.Trace("Song data: {0}", song);
        }
    }
}
