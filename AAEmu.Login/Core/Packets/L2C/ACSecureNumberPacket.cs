using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client requesting a specific security-card index from the user.
/// </summary>
/// <param name="secureCardIndex">The index on the user's security card to enter.</param>
public class ACSecureNumberPacket(ushort secureCardIndex) : LoginPacket(LCOffsets.ACSecureNumberPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(secureCardIndex);
        return stream;
    }
}