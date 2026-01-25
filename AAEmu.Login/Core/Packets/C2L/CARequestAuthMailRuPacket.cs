using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to request authentication via Mail.Ru credentials.
/// </summary>
public class CARequestAuthMailRuPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestAuthMailRuPacket;

    /// <summary>
    /// Gets the Mail.Ru user ID.
    /// </summary>
    public string? Id { get; private set; }

    /// <summary>
    /// Gets the authentication token provided by Mail.Ru.
    /// </summary>
    public byte[]? Token { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var dev = stream.ReadBoolean();
        var mac = stream.ReadBytes();
        Id = stream.ReadString();
        Token = stream.ReadBytes();
    }
}
