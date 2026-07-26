using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStopCastingPacket() : GamePacket(CSOffsets.CSStopCastingPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var tlId = stream.ReadUInt16(); // sid
        var plotTlId = stream.ReadUInt16(); // tl; pid
        var objId = stream.ReadBc();
        var character = Connection.ActiveChar;

        if (character.ObjId != objId)
        {
            Logger.Warn($"Player {character.Name} (ObjId {character.ObjId}) is trying to stop casting a skill on object {objId} using TlId {tlId} and plotTlId {plotTlId}");
            return;
        }

        var plotCancellationRequested = false;
        if (plotTlId != 0 && character.ActivePlotState != null)
        {
            if (character.ActivePlotState.ActiveSkill.TlId == plotTlId)
            {
                character.ActivePlotState.RequestCancellation();
                plotCancellationRequested = true;
            }
            else
            {
                Connection.SendPacket(new SCPlotCastingStoppedPacket(plotTlId, 0, 1));
                Connection.SendPacket(new SCPlotChannelingStoppedPacket(plotTlId, 0, 1));
            }
        }

        var skillTask = character.SkillTask;
        if (skillTask == null)
        {
            if (!plotCancellationRequested)
                Logger.Warn($"Stop requested, but no skill active? Tl: {tlId}, Pid: {plotTlId}, objId: {objId}, Character: {character.Name}");
            return;
        }

        var activeTlId = skillTask.Skill.TlId;
        var matchesSkillTask = tlId != 0
            ? activeTlId == tlId || activeTlId == plotTlId
            : plotTlId != 0 && activeTlId == plotTlId;

        if (!matchesSkillTask)
        {
            Logger.Warn($"Stop requested for another skill? Tl: {tlId}, Pid: {plotTlId}, ActiveTl: {activeTlId}, objId: {objId}, Character: {character.Name}");
            return;
        }

        skillTask.Cancel();

        if (skillTask is EndChannelingTask ect)
        {
            skillTask.Skill.Stop(character, ect._channelDoodad);
        }
        else
        {
            skillTask.Skill.Stop(character);
        }
    }
}
