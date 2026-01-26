using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

/// <summary>
/// A packet sent by the game server to register itself with the login server.
/// </summary>
public class GLRegisterGameServerPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLRegisterGameServerPacket;

    /// <summary>
    /// Gets the secret key used for authentication between the game server and the login server.
    /// </summary>
    public string? SecretKey { get; private set; }

    /// <summary>
    /// Gets the ID of the game server being registered.
    /// </summary>
    public GameServerId GsId { get; private set; }

    /// <summary>
    /// Gets the list of mirror game server IDs associated with this game server.
    /// </summary>
    public List<GameServerId>? Mirrors { get; private set; }

    public override void Read(PacketStream stream)
    {
        SecretKey = stream.ReadString();
        GsId = new GameServerId(stream.ReadByte());
        var additionalesCount = stream.ReadInt32();
        var mirrors = new List<GameServerId>(additionalesCount);
        for (var i = 0; i < additionalesCount; i++)
            mirrors.Add(new GameServerId(stream.ReadByte()));

        Mirrors = mirrors;
    }
}
