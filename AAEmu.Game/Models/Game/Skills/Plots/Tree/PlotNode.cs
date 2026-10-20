using System.Diagnostics;
using AAEmu.Game;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Plots;
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Templates;

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

        RememberMoveOrigin(state);

        var (castingMs, channelingMs) = PlotChannelingRules.NextEdgeDurations(
            Event.NextEvents.Select(nextEvent => (nextEvent.Casting, nextEvent.Channeling, nextEvent.Delay)));
        var animCs = PlotNextEvent.AnimCsTimeMs(Event.Effects);
        var addAnimToCast = false;
        var addAnimToChannel = false;
        foreach (var nextEvent in Event.NextEvents)
        {
            if (!nextEvent.AddAnimCsTime)
                continue;
            if (nextEvent.Casting)
                addAnimToCast = true;
            if (nextEvent.Channeling)
                addAnimToChannel = true;
        }

        var castTime = state.Caster.ApplySkillModifiers(state.ActiveSkill, SkillAttribute.CastTime, castingMs) *
                       state.Caster.CastTimeMul;
        castTime = Math.Max(castTime, 0);
        castTime = PlotChannelingRules.IncludeAnimCsTime((int)castTime, addAnimToCast, animCs);
        channelingMs = PlotChannelingRules.IncludeAnimCsTime(channelingMs, addAnimToChannel, animCs);
        var castWire = PlotChannelingRules.ToPlotWireTime((int)castTime);
        var channelWire = PlotChannelingRules.ToPlotWireTime(channelingMs);

        if (castTime > 0)
            state.IsCasting = true;
        if (ParentNextEvent?.Casting ?? false)
        {
            state.IsCasting = false;
            // Fire moment: entered via casting edge and this node does not extend the cast bar.
            if (castTime <= 0)
                state.ActiveSkill?.ApplyPlotOnlyFireCosts(state.Caster);
        }

        if (SkillCooldownCancelRules.PlotCommitted(
                HasCommittingEffects(),
                openedCastBar: castTime > 0,
                fireMoment: (ParentNextEvent?.Casting ?? false) && castTime <= 0,
                enteredChannel: ParentNextEvent?.Channeling ?? false))
            state.MarkFired();

        if (ParentNextEvent?.Channeling ?? false)
            state.IsChanneling = false;
        else
            state.IsChanneling = true;

        if (Event.HasSpecialEffects() || castTime > 0 || channelingMs > 0 || Event.Conditions.Count > 0)
        {
            // The bar this packet advertises is the one kind 18 (casting_useable) measures its percent
            // bands against; see PlotState.CastWindowMs.
            state.BeginCastWindow(Math.Max((int)castTime, channelingMs), DateTime.UtcNow);

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

            // SCPlotEvent carries the concrete units selected by an Area/Random* node. Its PlotObject
            // target is often a synthetic position and therefore cannot stand in for this list.
            var targetUnitIds = PlotTargetInfo.RealTargetUnitIds(targetInfo.EffectedTargets);

            // Echo the client's own CSStartSkill.inputDirection (0 unless the skill ships
            // use_input_direction). The client orients its controller run and FX off this byte; a
            // server-derived yaw here sent Backdraft and Ceaseless Fire off at the body's rounded heading.
            var inputDirection = skill?.ClientInputDirection ?? 0;

            var packet = new SCPlotEventPacket(state.CastTlId, Event.Id, skill.Template.Id, casterPlotObj,
                targetPlotObj, unkId, castWire, flag, 0, targetUnitIds, inputDirection, channelWire);
            state.LastClientEvent = new PlotClientEvent
            {
                Tl = state.CastTlId,
                EventId = Event.Id,
                SkillId = skill.Template.Id,
                Caster = casterPlotObj,
                Target = targetPlotObj,
                UnkId = unkId,
                CastWire = castWire,
                Flag = flag,
                TargetUnitIds = targetUnitIds,
                ChannelWire = channelWire,
                InputDirection = inputDirection
            };

            if (packets != null)
                packets.AddPacket(packet);
            else
            {
                state.Caster.BroadcastPacket(packet, true);
                if (RelayPlotEventToZoneIfNeeded(state.CastTlId, Event.Id, skill.Template.Id, casterPlotObj, targetPlotObj,
                        0ul, unkId, (uint)castTime, (uint)channelingMs, flag, targetUnitIds))
                    state.ZoneEventsRelayed = true;
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

    /// <summary>
    /// Effects that should lock the skill cooldown for this plot. Setup specials on the start
    /// node (GCD, auto-attack, stealth cancel, SetVariable) are ignored so a later Range fail
    /// can still refund the press-armed timer.
    /// </summary>
    private bool HasCommittingEffects()
    {
        foreach (var effect in Event?.Effects ?? [])
        {
            SpecialType? specialType = null;
            if (string.Equals(effect.ActualType, "SpecialEffect", StringComparison.Ordinal) &&
                SkillManager.Instance?.GetEffectTemplate(effect.ActualId, effect.ActualType) is SpecialEffect special)
                specialType = special.SpecialEffectTypeId;

            if (SkillCooldownCancelRules.CountsTowardPlotCommit(effect.ActualType, specialType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// First move controller that tells the plot to wait captures the caster where the shot was fired.
    /// The leap itself has not stepped yet; a later controller must not overwrite that pose.
    /// </summary>
    private void RememberMoveOrigin(PlotState state)
    {
        if (state == null || state.HasMoveOrigin || state.Caster == null)
            return;

        foreach (var eff in Event.Effects)
        {
            if (eff.ActualType != "SkillController")
                continue;
            if (SkillManager.Instance?.GetEffectTemplate(eff.ActualId, eff.ActualType) is not SkillControllerTemplate controller)
                continue;
            if (!PlotMoveOriginRules.RemembersOrigin(controller.Value[4]))
                continue;
            state.RememberMoveOrigin(state.Caster);
            return;
        }
    }

    private static bool RelayPlotEventToZoneIfNeeded(
        ushort tl,
        uint eventId,
        uint skillId,
        PlotObject casterPlotObj,
        PlotObject targetPlotObj,
        ulong itemId,
        uint objId,
        uint castTimeMs,
        uint channelingTimeMs,
        byte flag,
        IReadOnlyList<uint> targetUnitIds)
    {
        if (!WorldIntegration.ZoneAuthority)
            return false;
        if (Environment.GetEnvironmentVariable("AAEMU_DISABLE_WZ_PLOT_EVENT") == "1")
            return false;
        if (WorldIntegration.RelayPlotEventToZone == null)
            return false;

        var zoneTargetUnitIds = targetUnitIds?.ToArray() ?? [];
        if (zoneTargetUnitIds.Length == 0 && targetPlotObj.Type == PlotObjectType.UNIT && targetPlotObj.UnitId != 0)
            zoneTargetUnitIds = [targetPlotObj.UnitId];

        WorldIntegration.RelayPlotEventToZone(
            tl,
            eventId,
            skillId,
            casterPlotObj,
            targetPlotObj,
            itemId,
            objId,
            castTimeMs,
            channelingTimeMs,
            true,
            false,
            zoneTargetUnitIds);
        return true;
    }
}
