using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to warn about account-related issues.
/// </summary>
public class ACAccountWarnedPacket() : LoginPacket(LCOffsets.ACAccountWarnedPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)0); // source
        stream.Write(""); // msg

        return stream;
    }
}
