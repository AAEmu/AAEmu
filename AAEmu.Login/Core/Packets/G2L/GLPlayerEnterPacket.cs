using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

/// <summary>
/// A packet sent by the game server to inform the login server about a player's attempt to enter the game.
/// </summary>
public class GLPlayerEnterPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLPlayerEnterPacket;

    /// <summary>
    /// Gets the connection ID associated with the player attempting to enter the game.
    /// </summary>
    public ConnectionId ConnectionId { get; private set; }

    /// <summary>
    /// Gets the game server ID where the player is attempting to enter.
    /// </summary>
    public GameServerId GsId { get; private set; }

    /// <summary>
    /// Gets the result of the player's attempt to enter the game.
    /// </summary>
    public byte Result { get; private set; }

    public override void Read(PacketStream stream)
    {
        ConnectionId = new ConnectionId(stream.ReadUInt32());
        GsId = new GameServerId(stream.ReadByte());
        Result = stream.ReadByte();
    }
}
