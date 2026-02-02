using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to initiate the certificate process.
/// </summary>
/// <param name="maximumTries">The maximum number of tries at entering the certificate number.</param>
/// <param name="currentTry">The current try at entering the certificate number.</param>
/// <remarks>
/// If <paramref name="currentTry"/> is 0 or 1, no error message is displayed. If greater than one, the client displays
/// the number of remaining attempts.
/// Displayed as currentTry-1/maximumTries in the client UI.
/// </remarks>
public class ACEnterPcCertPacket(int maximumTries = 0, int currentTry = 0) : LoginPacket(LCOffsets.ACEnterPcCertPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(maximumTries); // mt
        stream.Write(currentTry); // ct

        return stream;
    }

    /// <summary>
    /// Creates an initial certificate entry packet with no error message.
    /// </summary>
    public static ACEnterPcCertPacket CreateInitialPacket() => new(0, 0);

    /// <summary>
    /// Creates a failure certificate entry packet with the specified maximum tries and current try.
    /// </summary>
    /// <param name="maximumTries">The maximum number of tries at entering the certificate number.</param>
    /// <param name="currentTry">The current try at entering the certificate number.</param>
    public static ACEnterPcCertPacket CreateFailurePacket(int maximumTries, int currentTry) =>
        new(maximumTries, currentTry);
}
