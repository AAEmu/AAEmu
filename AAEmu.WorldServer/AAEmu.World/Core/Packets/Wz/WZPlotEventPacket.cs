using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.World.Core.Packets.Wz;
/// <summary>
/// Mirrors SCPlotEvent fields; ZonePlotMan::PlayEvent consumes this.
/// </summary>
public class WZPlotEventPacket(
    ushort tl,
    uint eventId,
    uint skillId,
    PlotObject caster,
    PlotObject target,
    ulong itemId,
    uint objId,
    uint castTimeMs,
    uint channelingTimeMs,
    bool conditionOk,
    bool last,
    uint[] targetUnitIds)
    : ZonePacket(WzOpcodes.PlotEvent)
{
    protected override void WriteBody(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(eventId);
        stream.Write(skillId);
        stream.Write(caster);
        stream.Write(target);
        stream.Write(itemId);
        stream.WriteBc(objId);
        stream.Write(castTimeMs);
        stream.WriteBc(0u);
        stream.Write(channelingTimeMs);
        stream.Write(conditionOk);
        stream.Write(last);

        var ids = targetUnitIds ?? [];
        var count = ids.Length;
        stream.Write((uint)count);
        for (var i = 0; i < count; i++)
            stream.WriteBc(ids[i]);
    }
}
