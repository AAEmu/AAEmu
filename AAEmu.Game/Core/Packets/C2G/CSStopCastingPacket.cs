using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks.Skills;
using AAEmu.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStopCastingPacket() : GamePacket(CSOffsets.CSStopCastingPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16(); // sid
        var plotTlId = stream.ReadUInt16(); // tl; pid
        var objId = stream.ReadBc();

        if (Connection.ActiveChar == null)
            return;

        if (Connection.ActiveChar.ObjId != objId)
        {
            Logger.Warn($"Player {Connection.ActiveChar.Name} (ObjId {Connection.ActiveChar.ObjId}) is trying to stop casting a skill on object {objId} using TlId {tlId} and plotTlId {plotTlId}");
            return;
        }

        // World still owns the cast timer (CastTask → SpawnSlave etc.) even under ZoneAuthority.
        // Ignoring CSStopCasting left cancelled summons firing after the player started a new one —
        // multiple hulls, despawn races, and "ghost" ships. Cancel local state and tell Zone.

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

        var skillTask = Connection.ActiveChar.SkillTask;
        if (skillTask?.Skill == null)
        {
            Logger.Debug("StopCasting: no SkillTask tl={0} plotTl={1} char={2}", tlId, plotTlId, Connection.ActiveChar.Name);
            // Still notify Zone — it may be running a timeline World already lost track of.
            if (WorldIntegration.ZoneAuthority && tlId != 0)
                WorldIntegration.RelayCastingStoppedToZone?.Invoke(objId, (short)tlId, 0, 0);
            return;
        }

        // Client cancelled an older cast after starting a new one: SkillTask now points at the new
        // skill. Only stop when Tl matches; otherwise just tell Zone about the cancelled timeline.
        if (skillTask.Skill.TlId != tlId)
        {
            Logger.Warn(
                "StopCasting tl mismatch requested={0} active={1} char={2}",
                tlId, skillTask.Skill.TlId, Connection.ActiveChar.Name);
            if (WorldIntegration.ZoneAuthority)
                WorldIntegration.RelayCastingStoppedToZone?.Invoke(objId, (short)tlId, 0, 0);
            return;
        }

        skillTask.Cancel();

        if (skillTask is EndChannelingTask ect)
            skillTask.Skill.Stop(Connection.ActiveChar, ect._channelDoodad);
        else
            skillTask.Skill.Stop(Connection.ActiveChar);

        Logger.Info("StopCasting cancelled tl={0} skill={1} char={2}", tlId, skillTask.Skill.Id, Connection.ActiveChar.Name);
    }
}
