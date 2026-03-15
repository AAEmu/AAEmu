using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// Sent by the login server as part of the V2 Korea authentication challenge.
/// The client uses the provided <paramref name="round"/> and <paramref name="salt"/> to derive
/// the AES-256 key via sha256_crypt, then computes the challenge response.
/// </summary>
public class ACChallenge2Packet(int round, string salt, uint[] hc) : LoginPacket(LCOffsets.ACChallenge2Packet)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(round);
        stream.Write(Encoding.ASCII.GetBytes(salt), appendSize: true);
        for (var i = 0; i < 8; i++)
            stream.Write(i < hc.Length ? hc[i] : 0u);

        return stream;
    }
}
