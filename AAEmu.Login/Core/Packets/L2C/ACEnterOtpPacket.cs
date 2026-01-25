using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to prompt for OTP (One-Time Password) entry.
/// </summary>
public class ACEnterOtpPacket() : LoginPacket(LCOffsets.ACEnterOtpPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(0); // mt
        stream.Write(0); // ct

        return stream;
    }
}
