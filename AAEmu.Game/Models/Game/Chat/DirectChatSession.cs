using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Chat;

/// <summary>A server-owned one-to-one ("one and one") chat session between two characters.</summary>
/// <remarks>
/// The session id is assigned by the server when the conversation is opened and handed to both
/// clients in SCOneAndOneChatStartPacket; every CSOneAndOneChatAddMessagePacket that follows
/// quotes it back. The client treats it as opaque - it only uses it to key the per-contact window
/// and to echo it in the matching events - so it never has to be derivable from anything the
/// client knows, and it never leaves the server's memory: there is no shipped table for
/// one-to-one chat state, so a session dies with the connection that owned it.
/// </remarks>
public class DirectChatSession
{
    public long Id { get; init; }
    public Character CharacterA { get; init; }
    public Character CharacterB { get; init; }

    public bool Involves(uint characterId) =>
        CharacterA?.Id == characterId || CharacterB?.Id == characterId;

    /// <summary>The other participant, or null when <paramref name="character"/> is not in this session.</summary>
    public Character PeerOf(Character character)
    {
        if (character == null)
            return null;
        if (character.Id == CharacterA?.Id)
            return CharacterB;
        return character.Id == CharacterB?.Id ? CharacterA : null;
    }
}
