using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

/// <summary>
/// A packet sent by the game server to the login server to inform it that a player is reconnecting.
/// </summary>
public class GLPlayerReconnectPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLPlayerReconnectPacket;

    /// <summary>
    /// Gets the game server ID where the player is reconnecting.
    /// </summary>
    public GameServerId GsId { get; private set; }

    /// <summary>
    /// Gets the account ID of the player who is reconnecting.
    /// </summary>
    public AccountId AccountId { get; private set; }

    /// <summary>
    /// Gets the reconnection token for the player.
    /// </summary>
    public uint Token { get; private set; }

    public override void Read(PacketStream stream)
    {
        GsId = new GameServerId(stream.ReadByte());
        AccountId = new AccountId(stream.ReadUInt64());
        Token = stream.ReadUInt32();
    }
}
