using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Trailing <c>inputDirection</c> u8 is mandatory; omitting it desyncs the SC stream
/// (<c>sc error; cur=227 prev=…</c> → System:Quit).
/// </summary>
public class SCPlotEventPacket(
    ushort tl,
    uint eventId,
    uint skillId,
    PlotObject caster,
    PlotObject target,
    uint objId,
    ushort castingTime,
    byte flag,
    ulong itemId = 0L,
    byte targetUnitCount = 1,
    byte inputDirection = 0)
    : GamePacket(SCOffsets.SCPlotEventPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);      // u16 tl
        stream.Write(eventId); // u32 eventId
        stream.Write(skillId); // u32 skillId
        stream.Write(caster);  // PlotObj (type1=bc / type2=pos+rots+bcs)
        stream.Write(target);
        stream.Write(itemId);  // u64 item
        stream.WriteBc(objId);
        stream.Write(castingTime);
        stream.WriteBc(0);
        stream.Write((ushort)0); // channeling msec wire
        stream.Write(targetUnitCount);
        if (targetUnitCount > 0)
        {
            for (var i = 0; i < targetUnitCount; i++)
                stream.WriteBc(target.UnitId);
        }
        stream.Write(flag);
        if ((flag & 8) != 0)
        {
            for (var i = 0; i < 13; i++)
                stream.Write(0);
        }
        stream.Write(inputDirection); // ALWAYS present — was missing; caused client quit on 10752
        return stream;
    }
}
