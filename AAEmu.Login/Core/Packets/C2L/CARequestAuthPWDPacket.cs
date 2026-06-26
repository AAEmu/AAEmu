using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to submit a password (already encoded/hashed by the client) as part of authentication.
/// </summary>
public class CARequestAuthPWDPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestAuthPWDPacket;

    /// <summary>
    /// Gets the password value provided by the client.
    /// </summary>
    public string? Password { get; private set; }

    public override void Read(PacketStream stream)
    {
        Password = stream.ReadString();
    }
}