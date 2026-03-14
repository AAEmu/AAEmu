using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// Sent by the login server as part of the V1 Korea authentication challenge.
/// </summary>
/// <remarks>
/// <b>NOTE:</b> V1 challenge authentication is not enabled — SHA-1 without a per-account salt is
/// approximately 17 million times weaker than PBKDF2@600k. This packet type is preserved for
/// protocol completeness only. The server never issues this packet in production.
/// </remarks>
public class ACChallengePacket(uint salt, uint[] hc) : LoginPacket(LCOffsets.ACChallengePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(salt);
        for (var i = 0; i < 4; i++)
            stream.Write(i < hc.Length ? hc[i] : 0u);

        return stream;
    }
}
