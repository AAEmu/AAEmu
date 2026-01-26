using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to initiate the certificate process.
/// </summary>
public class ACEnterPcCertPacket() : LoginPacket(LCOffsets.ACEnterPcCertPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(0); // mt
        stream.Write(0); // ct

        return stream;
    }
}
