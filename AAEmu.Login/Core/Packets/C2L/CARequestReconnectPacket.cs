using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to request reconnection to a game server.
/// </summary>
public class CARequestReconnectPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestReconnectPacket;

    /// <summary>
    /// Gets the game server ID to which the client wants to reconnect.
    /// </summary>
    public GameServerId GsId { get; private set; }

    /// <summary>
    /// Gets the account ID of the client requesting reconnection.
    /// </summary>
    public AccountId AccountId { get; private set; }

    /// <summary>
    /// Gets the cookie used for authentication during reconnection.
    /// </summary>
    public uint Cookie { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        AccountId = new AccountId(stream.ReadUInt64());
        GsId = new GameServerId(stream.ReadByte()); // wid
        Cookie = stream.ReadUInt32();
        var macLength = stream.ReadUInt16();
        var mac = stream.ReadBytes(macLength);
    }
}
