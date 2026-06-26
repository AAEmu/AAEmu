using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to submit a security-card number (보안카드) for verification.
/// </summary>
public class CARequestVarifySNPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestVarifySNPacket;

    /// <summary>
    /// Gets the security-card number entered by the user.
    /// </summary>
    public string? SecurityNumber { get; private set; }

    public override void Read(PacketStream stream)
    {
        SecurityNumber = stream.ReadString();
    }
}