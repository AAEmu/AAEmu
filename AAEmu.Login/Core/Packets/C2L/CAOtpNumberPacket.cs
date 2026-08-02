using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server containing the OTP (One-Time Password) number for authentication.
/// </summary>
public class CAOtpNumberPacket() : LoginPacket(TypeId), ILoginPacket
{
    private const int MaximumOtpLength = 8;

    public new static ushort TypeId => CLOffsets.CAOtpNumberPacket;

    public string? OtpNumber { get; private set; }

    public override void Read(PacketStream stream)
    {
        OtpNumber = stream.ReadString();
        var byteLength = System.Text.Encoding.UTF8.GetByteCount(OtpNumber);
        if (byteLength is 0 or > MaximumOtpLength)
            throw new InvalidDataException($"OTP number must contain 1-{MaximumOtpLength} bytes");
    }
}
