using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client in response to a successful authentication request.
/// </summary>
/// <param name="accountId">The unique identifier of the account.</param>
/// <param name="slotCount">The number of character slots available to the account.</param>
/// <param name="webSessionKey">Web session key (wsk) — used by web/launcher auth; empty for standard login.</param>
/// <param name="encKey">Session encryption key — empty when unused.</param>
/// <param name="userKey">User key — empty when unused.</param>
/// <param name="countryCode">ISO country code (max 2 chars) — empty when unused.</param>
public class ACAuthResponsePacket(
    AccountId accountId,
    byte slotCount,
    string webSessionKey = "",
    string encKey = "",
    string userKey = "",
    string countryCode = "") : LoginPacket(LCOffsets.ACAuthResponsePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)accountId.Value); // accountId is a uint64 on the wire
        stream.Write(webSessionKey);          // wsk (length-prefixed string, max 32)
        stream.Write(slotCount);
        stream.Write(encKey);                 // encKey (length-prefixed string, max 127)
        stream.Write(userKey);                // userKey (length-prefixed string, max 15)
        stream.Write(countryCode);            // countryCode (length-prefixed string, max 2)

        return stream;
    }
}