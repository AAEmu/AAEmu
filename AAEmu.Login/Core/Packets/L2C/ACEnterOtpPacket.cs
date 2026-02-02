using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to prompt for OTP (One-Time Password) entry.
/// </summary>
/// <param name="maximumTries">The maximum number of tries at entering the one-time password.</param>
/// <param name="currentTry">The current try at entering the one-time password.</param>
/// <remarks>
/// If <paramref name="currentTry"/> is 0 or 1, no error message is displayed. If greater than one, the client displays
/// the number of remaining attempts.
/// Displayed as currentTry-1/maximumTries in the client UI.
/// </remarks>
public class ACEnterOtpPacket(int maximumTries = 0, int currentTry = 0) : LoginPacket(LCOffsets.ACEnterOtpPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(maximumTries); // mt
        stream.Write(currentTry); // ct

        return stream;
    }

    /// <summary>
    /// Creates an initial OTP entry packet with no error message.
    /// </summary>
    public static ACEnterOtpPacket CreateInitialPacket() => new(0, 0);

    /// <summary>
    /// Creates a failure OTP entry packet with the specified maximum tries and current try.
    /// </summary>
    /// <param name="maximumTries">The maximum number of tries at entering the one-time password.</param>
    /// <param name="currentTry">The current try at entering the one-time password.</param>
    public static ACEnterOtpPacket CreateFailurePacket(int maximumTries, int currentTry) =>
        new(maximumTries, currentTry);
}
