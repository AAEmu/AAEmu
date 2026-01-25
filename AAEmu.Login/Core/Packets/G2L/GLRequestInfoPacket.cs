using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

/// <summary>
/// A packet sent by the game server and containing the list of characters that an account has on that server.
/// </summary>
/// <remarks>
/// This is sent in response to a request for information about an account's characters made by the login server
/// using <see cref="LGRequestInfoPacket"/>.
/// </remarks>
/// <seealso cref="LGRequestInfoPacket"/>
public class GLRequestInfoPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLRequestInfoPacket;

    /// <summary>
    /// Gets the connection ID associated with the account whose character information was requested.
    /// </summary>
    public ConnectionId ConnectionId { get; private set; }

    /// <summary>
    /// Gets the request ID that matches the original request from the login server.
    /// </summary>
    public uint RequestId { get; private set; }

    /// <summary>
    /// Gets the list of characters that belong to the account.
    /// </summary>
    public List<LoginCharacterInfo>? Characters { get; private set; }

    public override void Read(PacketStream stream)
    {
        ConnectionId = new ConnectionId(stream.ReadUInt32());
        RequestId = stream.ReadUInt32();
        Characters = stream.ReadCollection<LoginCharacterInfo>();
    }
}
