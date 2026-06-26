using AAEmu.Commons.Cryptography;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class X2EnterWorldResponsePacket(short reason, bool gm, uint token, ushort port, GameConnection connection)
    : GamePacket(SCOffsets.X2EnterWorldResponsePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        // Field layout verified against the original server serializer (crynetwork_dedicate sub_39575070):
        //   reason(u16) sc(u32) sp(u16) wf(u64) tz(u32) pubKeySize(u16) pubKey[pubKeySize bytes] natAddr(i32) natPort(u16) authority(i32)
        // This packet is the RSA key-exchange trigger: sending an empty pubKey leaves the client unable to
        // complete the handshake (it then hangs). pubKeySize MUST be 260.
        stream.Write(reason);                        // reason (u16)
        stream.Write(token);                         // sc  — stream token (u32)
        stream.Write(port);                          // sp  — stream port (u16)
        stream.Write(Helpers.UnixTimeNow());         // wf  — server time (u64)
        stream.Write(0xffffff4cu);                   // tz  (u32)
        stream.Write(EncryptionManager.PubKeySize);  // pubKeySize (u16) = 260 (outer field)
        // The client deserializer (sub_39575200) reads pubKey via the length-prefixed blob method (+464):
        // [u16 innerLen][innerLen bytes]. RUNTIME-VERIFIED: without this inner length the client read the
        // blob's first 2 bytes (dwKeySize low = 0x0400 = 1024 > max 260) → read 0 bytes → zero pubKey → RSA
        // crash. The working 5.0 server writes pubKeySize TWICE for exactly this reason.
        stream.Write(EncryptionManager.PubKeySize);  // innerLen (u16) = 260 — blob length prefix
        EncryptionManager.Instance.WriteKeyParams(connection.Id, connection.AccountId, stream); // pubKey blob (260 bytes)
        stream.Write(0x0100007Fu);                   // natAddr (i32) = 127.0.0.1
        stream.Write(port);                          // natPort (u16) = stream port
        stream.Write((uint)1);                       // authority (i32)
        return stream;
    }
}
