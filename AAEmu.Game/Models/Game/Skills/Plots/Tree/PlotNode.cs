using System.Diagnostics;
using AAEmu.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Static;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotNode
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    // Tree
    public PlotTree Tree;
    public PlotNode Parent;
    public List<PlotNode> Children = [];
    // Plots
    public PlotEventTemplate Event;
    public PlotNextEvent ParentNextEvent;

    private bool IsChannelStart()
    {
        foreach (var child in Children)
        {
            if (child.ParentNextEvent.Channeling == true)
                return true;
        }
        return false;
    }

    public int ComputeDelayMs(PlotState state, PlotTargetInfo targetInfo)
    {
        return ParentNextEvent.GetDelay(state, targetInfo, Parent);
    }

    public bool CheckConditions(PlotState state, PlotTargetInfo targetInfo)
    {
        return Event.Conditions.All(condition => condition.CheckCondition(state, targetInfo));
    }

    public void Execute(PlotState state, PlotTargetInfo targetInfo, CompressedGamePackets packets = null)
    {
        //Logger.Debug("Executing plot node with id {0}", Event.Id);

        var stopwatch = new Stopwatch();
        stopwatch.Start();
        byte flag = 2;
        var deferredActions = new List<Action>();
        // Exclude synthetic ObjId=MaxValue position anchors from the Area/RandomArea hit count.
        state.LastEffectedTargetCount = targetInfo.EffectedTargets.Count(
            t => t != null && t.ObjId != 0 && t.ObjId != uint.MaxValue);
        foreach (var eff in Event.Effects)
        {
            try
            {
                eff.ApplyEffect(state, targetInfo, Event, ref flag, IsChannelStart(),
                    deferUntilPlotEventProcessed: deferredActions.Add);
            }
            catch (Exception e)
            {
                state?.Caster?.SendPacket(new SCChatMessagePacket(Chat.ChatType.Notice, "Plot Effects Error - Check Logs"));
                Logger.Error("[Plot Effects Error]: {0}\n{1}", e.Message, e.StackTrace);
            }
        }

        double castTime = Event.NextEvents
             .Where(nextEvent => nextEvent.Casting || nextEvent.Channeling)
             .Max(nextEvent => nextEvent.Delay / 10 as int?) ?? 0;
        castTime = state.Caster.ApplySkillModifiers(state.ActiveSkill, SkillAttribute.CastTime, castTime) * state.Caster.CastTimeMul;
        castTime = Math.Max(castTime, 0);

        if (castTime > 0)
            state.IsCasting = true;
        if (ParentNextEvent?.Casting ?? false)
        {
            state.IsCasting = false;
            // Fire moment: entered via casting edge and this node does not extend the cast bar.
            if (castTime <= 0)
                state.ActiveSkill?.ApplyPlotOnlyFireCosts(state.Caster);
        }

        if (ParentNextEvent?.Channeling ?? false)
            state.IsChanneling = false;
        else
            state.IsChanneling = true;

        if (Event.HasSpecialEffects() || castTime > 0 || Event.Conditions.Count > 0)
        {
            var skill = state.ActiveSkill;
            var unkId = (ParentNextEvent?.Casting ?? false) || (ParentNextEvent?.Channeling ?? false) ? state.Caster.ObjId : 0;

            PlotObject casterPlotObj;
            if (targetInfo.Source.ObjId == uint.MaxValue)
                casterPlotObj = new PlotObject(targetInfo.Source.Transform);
            else
                casterPlotObj = new PlotObject(targetInfo.Source);

            PlotObject targetPlotObj;
            if (targetInfo.Target.ObjId == uint.MaxValue)
                targetPlotObj = new PlotObject(targetInfo.Target.Transform);
            else
                targetPlotObj = new PlotObject(targetInfo.Target);

            var targetCount = (byte)targetInfo.EffectedTargets.Count;

            var packet = new SCPlotEventPacket(skill.TlId, Event.Id, skill.Template.Id, casterPlotObj,
                targetPlotObj, unkId, (ushort)castTime, flag, 0, targetCount);

            if (packets != null)
                packets.AddPacket(packet);
            else
            {
                state.Caster.BroadcastPacket(packet, true);
                RelayPlotEventToZoneIfNeeded(skill.TlId, Event.Id, skill.Template.Id, casterPlotObj, targetPlotObj,
                    0ul, unkId, (ushort)castTime, flag, targetCount, targetInfo);
            }

            Logger.Trace($"Execute Took {stopwatch.ElapsedMilliseconds} to finish.");
        }

        foreach (var action in deferredActions)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                state?.Caster?.SendPacket(new SCChatMessagePacket(
                    Chat.ChatType.Notice,
                    "Post-plot action error - check logs"));
                Logger.Error(e, "Post-plot action failed for event {0}", Event.Id);
            }
        }
    }

    private static void RelayPlotEventToZoneIfNeeded(
        ushort tl,
        uint eventId,
        uint skillId,
        PlotObject casterPlotObj,
        PlotObject targetPlotObj,
        ulong itemId,
        uint objId,
        ushort castingTime,
        byte flag,
        byte targetUnitCount,
        PlotTargetInfo targetInfo)
    {
        if (!WorldIntegration.ZoneAuthority)
            return;
        if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_WZ_PLOT_EVENT") == "1")
            return;
        if (WorldIntegration.RelayPlotEventToZone == null)
            return;

        var targetIds = new List<uint>();
        if (targetUnitCount > 0 && targetInfo.EffectedTargets.Count > 0)
        {
            foreach (var t in targetInfo.EffectedTargets)
            {
                // Area/RandomArea synthetic targets use ObjId=MaxValue — not valid bc ids.
                if (t.ObjId != 0 && t.ObjId != uint.MaxValue)
                    targetIds.Add(t.ObjId);
            }
        }
        else if (targetPlotObj.Type == PlotObjectType.UNIT && targetPlotObj.UnitId != 0)
            targetIds.Add(targetPlotObj.UnitId);

        WorldIntegration.RelayPlotEventToZone(
            tl,
            eventId,
            skillId,
            casterPlotObj,
            targetPlotObj,
            itemId,
            objId,
            castingTime,
            0u,
            true,
            false,
            targetIds.ToArray());
    }
}
