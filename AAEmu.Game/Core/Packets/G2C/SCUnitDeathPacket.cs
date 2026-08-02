using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCUnitDeathPacket(uint objId, KillReason killReason, Unit killer = null)
    : GamePacket(SCOffsets.SCUnitDeathPacket, 1)
{
    //   Bc victim + u8 killReason
    //   + u32 resurrectionWaitingTime
    //   + u32 specialResurrectionWaitingTime
    //   + u32 autoResurrectionWaitingTime
    //   + i32 lostExp + u8 deathDurabilityLossRatio
    //   + Bc killer; if killer != <no-killer sentinel>:
    //       u8 GameType + u16 killStreak + u8 param1 + u8 param2 + u32 type + string killerName
    //
    // 0xFFFFFF, the client took the killer branch on a body that has no killer block, and ran off the
    // end — the "sc error; cur=161" that fired every time a book portal timed out.
    private const uint NoKillerObjId = 0u;

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write((byte)killReason);

        stream.Write(15000u); // resurrectionWaitingTime
        stream.Write(0u);     // specialResurrectionWaitingTime
        stream.Write(0u);     // autoResurrectionWaitingTime
        stream.Write(0);      // lostExp
        stream.Write((byte)0); // deathDurabilityLossRatio

        var killerId = killer?.ObjId ?? NoKillerObjId;
        stream.WriteBc(killerId);
        if (killerId != NoKillerObjId)
        {
            stream.Write((byte)0);   // GameType
            stream.Write((ushort)0); // killStreak
            stream.Write((byte)0);   // param1
            stream.Write((byte)0);   // param2
            stream.Write(0u);        // type (u32 — was wrongly u8 param3)
            stream.Write(killer?.Name ?? string.Empty);
        }

        return stream;
    }
}
