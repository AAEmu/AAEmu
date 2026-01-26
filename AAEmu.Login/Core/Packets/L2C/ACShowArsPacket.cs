using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to show ARS information.
/// </summary>
public class ACShowArsPacket() : LoginPacket(LCOffsets.ACShowArsPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(""); // num
        stream.Write((uint)0); // timeout

        return stream;
    }
}
