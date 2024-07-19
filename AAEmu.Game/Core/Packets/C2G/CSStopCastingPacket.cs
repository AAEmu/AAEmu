using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks.Skills;
using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStopCastingPacket : GamePacket
{
    public CSStopCastingPacket() : base(CSOffsets.CSStopCastingPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16(); // sid
        var plotTlId = stream.ReadUInt16(); // tl; pid
        var objId = stream.ReadBc();

        if (Connection.ActiveChar.ObjId != objId)
        {
            Logger.Warn($"Player {Connection.ActiveChar.Name} (ObjId {Connection.ActiveChar.ObjId}) is trying to stop casting a skill on object {objId} using TlId {tlId} and plotTlId {plotTlId}");
            return;
        }

        if (plotTlId != 0 && Connection.ActiveChar.ActivePlotState != null)
        {
            if (Connection.ActiveChar.ActivePlotState.ActiveSkill.TlId == plotTlId)
            {
                Connection.ActiveChar.ActivePlotState.RequestCancellation();
            }
            else
            {
                Connection.SendPacket(new SCPlotCastingStoppedPacket(plotTlId, 0, 1));
                Connection.SendPacket(new SCPlotChannelingStoppedPacket(plotTlId, 0, 1));
            }
        }

        if (Connection.ActiveChar.SkillTask == null || Connection.ActiveChar.SkillTask.Skill.TlId != tlId)
        {
            Logger.Warn($"Stop requested, but no skill active? Tl: {tlId}, Pid: {plotTlId}, objId: {objId}, Character: {Connection.ActiveChar.Name}");
            return;
        }

        Connection.ActiveChar.SkillTask.Cancel();

        if (Connection.ActiveChar.SkillTask is EndChannelingTask ect)
        {
            Connection.ActiveChar.SkillTask.Skill.Stop(Connection.ActiveChar, ect._channelDoodad);
        }
        else
        {
            Connection.ActiveChar.SkillTask.Skill.Stop(Connection.ActiveChar);
        }
    }
}
