using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Core.Packets.G2C;

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
    byte targetUnitCount = 1)
    : GamePacket(SCOffsets.SCPlotEventPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);      // tl
        stream.Write(eventId); // eventId
        stream.Write(skillId); // skillId
        stream.Write(caster);  // PlotObj
                                // type(b) Unit | Position
                                // casterId(bc) | XYZ
        stream.Write(target);  // PlotObj
                                // type(b) Unit | Position
                                // targetId(bc) | XYZ
        stream.Write(itemId);  // itemObjId
        stream.WriteBc(objId); // обычно 0, но иногда нужно вставлять casterId(bc)
        stream.Write(castingTime); // msec, castingTime / 10
        stream.WriteBc(0);      // objId
        stream.Write((short)0); // msec
        stream.Write(targetUnitCount); // targetUnitCount // TODO if aoe, list of units
        if (targetUnitCount > 0)
        {
            for (var i = 0; i < targetUnitCount; i++)
            {
                stream.WriteBc(target.UnitId); // targetId TODO targetUnitCount > 0 -> do->while() stream.WriteBc(0);
            }
        }
        stream.Write(flag);
        if (((flag >> 3) & 1) != 1)
        {
            return stream;           // We had a note here that flag = 2 | 6, but it can also be 0. It defaults to 2, it seems.
        }
        for (var i = 0; i < 13; i++) // flag = 8
        {
            stream.Write(0); // v
        }
        return stream;
    }
}
