using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client containing the RSA public key used to encrypt the password.
/// </summary>
/// <param name="modulus">The RSA modulus, as a string (max 128 chars).</param>
/// <param name="exponent">The RSA public exponent.</param>
public class ACPublicKeyResponsePacket(string modulus, int exponent)
    : LoginPacket(LCOffsets.ACPublicKeyResponsePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(modulus);   // modulus (length-prefixed string, max 128)
        stream.Write(exponent);  // exponent (int32)
        return stream;
    }
}