using System.Diagnostics;
using AAEmu.Game;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotTree(uint plotId)
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint PlotId { get; set; } = plotId;

    public PlotNode RootNode { get; set; }

    public async Task ExecuteAsync(PlotState state)
    {
        var treeWatch = new Stopwatch();
        treeWatch.Start();
        Logger.Trace($"Executing plot tree with ID {PlotId}");
        try
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var schedule = new PlotSchedule<(PlotNode node, PlotTargetInfo targetInfo)>();
            var executeQueue = new Queue<(PlotNode node, PlotTargetInfo targetInfo)>();

            schedule.Enqueue((RootNode, new PlotTargetInfo(state)), DateTime.UtcNow);
            byte lastEvent = 1;
            while (schedule.Count > 0)
            {
                // Bite and cancel are polled once per pass rather than once per due node: the wait for a
                // channel edge is minutes long, and both the rod's bite and CSStopCasting have to land
                // inside it. Everything below only runs when the earliest node is actually due.
                if (state.IsChanneling && state.ChannelingFinishRequested())
                {
                    ResumeChannelEnd(state, schedule);
                    lastEvent = 0;
                    continue;
                }
                if (state.CancellationRequested())
                {
                    var stoppedChannel = ResumeChannelEnd(state, schedule);
                    if (state.IsCasting || stoppedChannel)
                    {
                        if (state.IsCasting)
                        {
                            state.Caster.BroadcastPacket(
                                new SCPlotCastingStoppedPacket(state.CastTlId, 0, lastEvent),
                                true
                            );
                        }

                        state.Caster.BroadcastPacket(
                            new SCPlotChannelingStoppedPacket(state.CastTlId, 0, 1),
                            true
                        );
                    }

                    DoPlotEnd(state);
                    return;
                }

                if (!schedule.TryDequeueDue(DateTime.UtcNow, out var item))
                {
                    var wait = PlotSchedule<(PlotNode, PlotTargetInfo)>.WaitSliceMs(
                        schedule.NextDueUtc, DateTime.UtcNow);
                    if (wait > 0)
                        await Task.Delay(wait).ConfigureAwait(false);
                    continue;
                }

                var nodeWatch = new Stopwatch();
                nodeWatch.Start();
                var now = DateTime.UtcNow;
                var node = item.node;

                if (state.Tickets.TryGetValue(node.Event.Id, out var value))
                    state.Tickets[node.Event.Id] = ++value;
                else
                    state.Tickets.TryAdd(node.Event.Id, 1);

                var selfLoop = node.Children.Exists(c => c.Event.Id == node.Event.Id);
                if (PlotTicketGate.IsExhausted(
                        state.Tickets[node.Event.Id], node.Event.Tickets, selfLoop))
                {
                    continue;
                }

                item.targetInfo.UpdateTargetInfo(node.Event, state);

                if (item.targetInfo.Target == null)
                    continue;

                    // enum_plot_variable_kinds id 12 ("targets") is engine-provided: the hit count of
                    // THIS event's target update. Conditions run BEFORE Execute, so the count has to be
                    // published here as well — PlotNode.Execute recomputes the identical value, but by
                    // then the gate below has already branched on a stale number.
                    state.LastEffectedTargetCount = item.targetInfo.EffectedTargets.Count(
                        t => t != null && t.ObjId != 0 && t.ObjId != uint.MaxValue);

                    var condition = node.CheckConditions(state, item.targetInfo);

                    if (condition)
                    {
                        executeQueue.Enqueue((node, item.targetInfo));
                    }

                    // Apply this node's effects before child condition gates. Plot 5796/5604 do
                    // Area → SetVariable op 12 (hit count) → child Variable==0 ("no target").
                    // Deferred Execute left Variables[] at 0 for the whole zero-delay chain, so
                    // every gun-path cast took the no-target fail branch even with hostiles in range.
                    FlushExecutionQueue(executeQueue, state);

                    foreach (var child in PlotBranchRules.SelectChildren(node.Children, condition, Random.Shared.Next))
                    {
                        if (child.ParentNextEvent?.PerTarget ?? false)
                        {
                            foreach (var target in item.targetInfo.EffectedTargets)
                            {
                                var targetInfo = new PlotTargetInfo(item.targetInfo.Source, target);
                                schedule.Enqueue(
                                    (child, targetInfo),
                                    now.AddMilliseconds(child.ComputeDelayMs(state, targetInfo))
                                );
                            }
                        }
                        else
                        {
                            var targetInfo = new PlotTargetInfo(item.targetInfo.Source, item.targetInfo.Target);
                            schedule.Enqueue(
                                (child, targetInfo),
                                now.AddMilliseconds(child.ComputeDelayMs(state, targetInfo))
                            );
                        }
                    }

                if (nodeWatch.ElapsedMilliseconds > 100)
                    Logger.Trace($"Event:{node.Event.Id} Took {nodeWatch.ElapsedMilliseconds} to finish.");
            }

            FlushExecutionQueue(executeQueue, state);
        }
        catch (Exception e)
        {
            Logger.Error($"Main Loop Error: {e.Message}\n {e.StackTrace}");
        }

        DoPlotEnd(state);
        Logger.Trace($"Tree with ID {PlotId} has finished executing took {treeWatch.ElapsedMilliseconds}ms");
    }
    /// <summary>
    /// Bite and cancel both cut the channel wait. The wait is the child entered by a
    /// channeling edge — not the bite-roll loop queued next to it. Ending that wait runs
    /// hook/fail (ClearProjectile) so the cast line is torn down.
    /// </summary>
    private bool ResumeChannelEnd(
        PlotState state,
        PlotSchedule<(PlotNode node, PlotTargetInfo targetInfo)> schedule)
    {
        if (state == null || schedule == null)
            return false;

        // Drained in due order: the channel wait is the node entered by a channeling edge, whichever
        // position it holds on the timeline.
        var waiting = schedule.DrainAll();

        var index = PlotChannelingRules.IndexOfChannelWait(
            waiting,
            entry => entry.Item.node?.ParentNextEvent?.Channeling == true);
        if (index < 0)
        {
            foreach (var entry in waiting)
                schedule.Enqueue(entry.Item, entry.DueUtc);
            return false;
        }

        EndPlotChannel(state);
        state.PermitChanneling();

        var channelItem = waiting[index];
        var channelNode = channelItem.Item.node;
        channelNode.Execute(state, channelItem.Item.targetInfo);
        FollowChannelEnd(
            state,
            channelNode,
            channelItem.Item.targetInfo,
            schedule,
            enqueueDelayed: !state.CancellationRequested());

        return true;
    }

    private static void FollowChannelEnd(
        PlotState state,
        PlotNode parent,
        PlotTargetInfo targetInfo,
        PlotSchedule<(PlotNode node, PlotTargetInfo targetInfo)> schedule,
        bool enqueueDelayed)
    {
        var eligible = new List<PlotNode>(parent.Children?.Count ?? 0);
        foreach (var child in parent.Children ?? [])
        {
            if (child?.Event == null || child.ParentNextEvent == null)
                continue;

            var condition = child.CheckConditions(state, targetInfo);
            if (condition == child.ParentNextEvent.Fail)
                continue;

            eligible.Add(child);
        }

        foreach (var child in PlotBranchRules.SelectEligible(eligible, Random.Shared.Next))
        {
            var childInfo = new PlotTargetInfo(targetInfo.Source, targetInfo.Target);
            var delay = child.ComputeDelayMs(state, childInfo);
            if (delay > 0)
            {
                if (enqueueDelayed)
                    schedule.Enqueue((child, childInfo), DateTime.UtcNow.AddMilliseconds(delay));
                continue;
            }

            child.Execute(state, childInfo);
            FollowChannelEnd(state, child, childInfo, schedule, enqueueDelayed);
        }
    }
    private static void FlushExecutionQueue(Queue<(PlotNode node, PlotTargetInfo targetInfo)> executeQueue, PlotState state)
    {
        while (executeQueue.Count > 0)
        {
            var item = executeQueue.Dequeue();
            item.node.Execute(state, item.targetInfo);
        }
    }

    private static void EndPlotChannel(PlotState state)
    {
        foreach (var (unit, buffId) in state.ChanneledBuffs)
        {
            unit.Buffs.RemoveBuff(buffId);
        }
    }

    /// <summary>
    /// Ends a plot that has no tree to execute, through the same sequence <see cref="ExecuteAsync"/>
    /// finishes with. <see cref="PlotEndRules.ShouldEndWithoutTree"/> decides when this applies — 67 plots
    /// in 10.0.2.13 have no position-1 event, and 30 skills still cast them.
    /// </summary>
    /// <remarks>
    /// DoPlotEnd clears the caster's ActivePlotState but not the skill's — Plot.RunAsync sets
    /// Skill.ActivePlotState and the tree's own end path never had to undo it. Clear it here too, so a
    /// finished skill does not keep handing SetVariable / PlotCondition a plot state that is over; both
    /// read skill.ActivePlotState before the caster's.
    /// </remarks>
    public static void EndPlotWithoutTree(PlotState state)
    {
        if (state == null)
            return;

        DoPlotEnd(state);

        state.ActiveSkill?.ReleaseActivePlotState(state);
    }

    /// <summary>
    /// Drops a plot state that a cast path still owns: both <c>ActivePlotState</c> fields are cleared and
    /// nothing else happens. The cast's own <c>EndSkill</c> is what arms the cooldown, releases the TlId
    /// and fires <c>OnSkillEnd</c>, so doing any of that here would end the skill twice from a plot thread.
    /// </summary>
    public static void DropPlotState(PlotState state)
    {
        if (state == null)
            return;

        state.Caster?.ReleaseActivePlotState(state);
        state.ActiveSkill?.ReleaseActivePlotState(state);
    }

    private static void DoPlotEnd(PlotState state)
    {
        state.Caster?.BroadcastPacket(new SCPlotEndedPacket(state.CastTlId), true);
        EndPlotChannel(state);

        state.ActiveSkill.ArmCooldowns(state.Caster);

        if (state.Caster is Character { IgnoreSkillCooldowns: true } character)
            character.ResetSkillCooldown(state.ActiveSkill.Template.Id, false);

        // Maybe always do this on end of plot?
        // Should we check if it was a channeled skill?
        if (state.CancellationRequested())
            state.Caster?.Events.OnChannelingCancel(state.ActiveSkill, new OnChannelingCancelArgs());

        state.ActiveSkill.RelayZoneSkillEndedIfNeeded();
        SkillTlIdManager.ReleaseId(state.ActiveSkill.TlId);
        state.ActiveSkill.TlId = 0;

        state.Caster?.OnSkillEnd(state.ActiveSkill);
        state.ActiveSkill.Callback?.Invoke();
        state.Caster?.ReleaseActivePlotState(state);
    }
}
