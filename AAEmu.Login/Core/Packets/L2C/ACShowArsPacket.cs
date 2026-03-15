using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to show ARS (Automatic Response System/자동응답시스템) information.
/// </summary>
/// <param name="number">The ARS number displayed to the user in the game client.</param>
/// <param name="timeout">The timeout displayed next to the ARS number as remaining time.</param>
/// <remarks>
/// ARS is automated phone verification. The system calls your registered phone number, and you enter a code displayed
/// on screen. Since Korean phone numbers are tied to real identity, this serves as identity verification.
/// </remarks>
public class ACShowArsPacket(string number, TimeSpan timeout) : LoginPacket(LCOffsets.ACShowArsPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        // "Enter the number below when you get a call"
        stream.Write(number); // num - the text displayed to the user
        stream.Write((uint)timeout.TotalMilliseconds); // timeout - time in milliseconds displayed next to the text as remaining time

        return stream;
    }
}
