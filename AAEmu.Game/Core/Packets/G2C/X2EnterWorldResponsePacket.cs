using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class X2EnterWorldResponsePacket(short reason, uint token, ushort port, GameConnection connection)
    : GamePacket(SCOffsets.X2EnterWorldResponsePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        //   reason(u16) sc(u32) sp(u16) wf(u64) tz(u32) pubKeySize(u16) pubKey[pubKeySize bytes] natAddr(i32) natPort(u16) authority(i32)
        // This packet is the RSA key-exchange trigger: sending an empty pubKey leaves the client unable to
        // complete the handshake (it then hangs). pubKeySize MUST be 260.
        stream.Write(reason);                        // reason (u16)
        stream.Write(token);                         // sc  â€” stream token (u32)
        stream.Write(port);                          // sp  â€” stream port (u16)
        stream.Write(Helpers.UnixTimeNow());         // wf  â€” server time (u64)
        stream.Write(0xffffff4cu);                   // tz  (u32)
        stream.Write(EncryptionManager.PubKeySize);  // pubKeySize (u16) = 260 (outer field)
        // [u16 innerLen][innerLen bytes]. RUNTIME-VERIFIED: without this inner length the client read the
        // blob's first 2 bytes (dwKeySize low = 0x0400 = 1024 > max 260) â†’ read 0 bytes â†’ zero pubKey â†’ RSA
        // crash. The working 5.0 server writes pubKeySize TWICE for exactly this reason.
        stream.Write(EncryptionManager.PubKeySize);  // innerLen (u16) = 260 â€” blob length prefix
        EncryptionManager.Instance.WriteKeyParams(connection.Id, connection.AccountId, stream); // pubKey blob (260 bytes)
        stream.Write(0x0100007Fu);                   // natAddr (i32) = 127.0.0.1
        stream.Write(port);                          // natPort (u16) = stream port
        //   (authority & 1) != 0 â†’ use cl_serveraddr (login TGW IP)
        //   else â†’ inet_ntoa(hostlong) which is 0 for normal login â†’ addr 0.0.0.0 â†’ OpenStream fail.
        // This is a native bitmask, not AAEmu's numeric access level. In r575, bit 0x04
        // makes character creation require the engine's editor mode. Sending access
        // level 100 | 1 blocks normal GM clients before CSCreateCharacter. Keep the retail connection authority;
        // account/character access levels and server-side gmFlag authorize GM commands.
        stream.Write(1u);                            // authority (i32)
        return stream;
    }
}
