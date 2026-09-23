namespace AAEmu.Game.Models.Game.CrossServer;

/// <summary>
/// Resolves which peer server a departure is aimed at.
/// <para>
/// The pinned wire does not carry one: <c>CSDepartToForeignServerPacket</c> (0x1C7) has an empty
/// body in the 10.0.2.13 client serializer, so the destination can only come from content.
/// Implementations read the shipped <c>server_configs</c> table; content that does not name an
/// unambiguous peer returns <see langword="null"/> and the departure is refused loudly.
/// </para>
/// </summary>
public interface ICrossServerDirectory
{
    /// <summary>The peer server key for this server's id, or <see langword="null"/> when content offers no single peer.</summary>
    string ResolvePeerKey(byte ownServerId);
}
