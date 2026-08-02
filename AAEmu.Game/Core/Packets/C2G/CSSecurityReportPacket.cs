using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// CSSecurityReport (0x1AD) — the client's own integrity report. Named after the client's
///
///   1: u32, u64, string
///   2: string(&lt;=259), string
///   3: nested record, u32, u16
///   4: 34 x { u64 itemId, u32 evolveExp, 5 x { u16 unitAttribute, f32 value } },
///      then 2 x f32 mulValue, then u32 worldTime  — 1441 bytes total
///   5: string(&lt;=1023)
///   6: changed/original world XYZ + 5+5 gear ratios + u8 type
///
/// srType 4 is the one that arrives on a ~31s timer once the client finishes loading: it is the
/// client's view of its own equipment stat table so the server can compare it against its own.
/// It is fire-and-forget — the protocol has no ack, and the server-initiated challenges are
/// separate packets (SCGameGuardRequest 0x241, SCHackScanRequest 0x242) — so leaving it
/// unanswered is correct and does not stall or drop the client.
/// </summary>
public class CSSecurityReportPacket() : GamePacket(CSOffsets.CSSecurityReportPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var remaining = stream.Count - stream.Pos;
        var srType = remaining > 0 ? stream.ReadByte() : (byte)0;

        Logger.Debug("CSSecurityReport srType={0} len={1}", srType, remaining);

        remaining = stream.Count - stream.Pos;
        if (remaining > 0)
            stream.ReadBytes(remaining);
    }
}
