using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client in response to a privacy-policy request.
/// </summary>
/// <param name="response">The privacy-policy response code.</param>
public class ACPrivacyPolicyResponsePacket(byte response) : LoginPacket(LCOffsets.ACPrivacyPolicyResponsePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(response);
        return stream;
    }
}