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

            var queue = new Queue<(PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo)>();
            var executeQueue = new Queue<(PlotNode node, PlotTargetInfo targetInfo)>();

            queue.Enqueue((RootNode, DateTime.UtcNow, new PlotTargetInfo(state)));
            byte lastEvent = 1;
            while (queue.Count > 0)
            {
                var nodeWatch = new Stopwatch();
                nodeWatch.Start();
                var item = queue.Dequeue();
                var now = DateTime.UtcNow;
                var node = item.node;
                if (state.IsChanneling && state.ChannelingFinishRequested())
                {
                    ResumeChannelEnd(state, queue, item);
                    lastEvent = 0;
                    continue;
                }
                if (state.CancellationRequested())
                {
                    var stoppedChannel = ResumeChannelEnd(state, queue, item);
                    if (state.IsCasting || stoppedChannel)
                    {
                        if (state.IsCasting)
                        {
                            state.Caster.BroadcastPacket(
                                new SCPlotCastingStoppedPacket(state.ActiveSkill.TlId, 0, lastEvent),
                                true
                            );
                        }

                        state.Caster.BroadcastPacket(
                            new SCPlotChannelingStoppedPacket(state.ActiveSkill.TlId, 0, 1),
                            true
                        );
                    }

                    DoPlotEnd(state);
                    return;
                }

                if (now >= item.timestamp)
                {
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

                    foreach (var child in node.Children)
                    {
                        if (condition != child.ParentNextEvent.Fail)
                        {
                            if (child.ParentNextEvent?.PerTarget ?? false)
                            {
                                foreach (var target in item.targetInfo.EffectedTargets)
                                {
                                    var targetInfo = new PlotTargetInfo(item.targetInfo.Source, target);
                                    queue.Enqueue(
                                        (
                                        child,
                                        now.AddMilliseconds(child.ComputeDelayMs(state, targetInfo)),
                                        targetInfo
                                        )
                                    );
                                }
                            }
                            else
                            {
                                var targetInfo = new PlotTargetInfo(item.targetInfo.Source, item.targetInfo.Target);
                                queue.Enqueue(
                                    (
                                    child,
                                    now.AddMilliseconds(child.ComputeDelayMs(state, targetInfo)),
                                    targetInfo
                                    )
                                );
                            }
                        }
                    }
                }
                else
                {
                    queue.Enqueue((node, item.timestamp, item.targetInfo));
                    FlushExecutionQueue(executeQueue, state);
                }

                if (queue.Count > 0)
                {
                    var delay = (int)queue.Min(o => (o.timestamp - DateTime.UtcNow).TotalMilliseconds);
                    delay = Math.Max(delay, 0);

                    // await Task.Delay(delay).ConfigureAwait(false);
                    if (delay > 0)
                        await Task.Delay(15).ConfigureAwait(false);
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
        Queue<(PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo)> queue,
        (PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo) current)
    {
        if (state == null || queue == null)
            return false;

        var waiting = new List<(PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo)>(queue.Count + 1);
        waiting.Add(current);
        while (queue.Count > 0)
            waiting.Add(queue.Dequeue());

        var index = PlotChannelingRules.IndexOfChannelWait(
            waiting,
            item => item.node?.ParentNextEvent?.Channeling == true);
        if (index < 0)
        {
            foreach (var item in waiting)
                queue.Enqueue(item);
            return false;
        }

        EndPlotChannel(state);
        state.PermitChanneling();

        var channelItem = waiting[index];
        var channelNode = channelItem.node;
        channelNode.Execute(state, channelItem.targetInfo);
        FollowChannelEnd(
            state,
            channelNode,
            channelItem.targetInfo,
            queue,
            enqueueDelayed: !state.CancellationRequested());

        return true;
    }

    private static void FollowChannelEnd(
        PlotState state,
        PlotNode parent,
        PlotTargetInfo targetInfo,
        Queue<(PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo)> queue,
        bool enqueueDelayed)
    {
        foreach (var child in parent.Children ?? [])
        {
            if (child?.Event == null || child.ParentNextEvent == null)
                continue;

            var condition = child.CheckConditions(state, targetInfo);
            if (condition == child.ParentNextEvent.Fail)
                continue;

            var childInfo = new PlotTargetInfo(targetInfo.Source, targetInfo.Target);
            var delay = child.ComputeDelayMs(state, childInfo);
            if (delay > 0)
            {
                if (enqueueDelayed)
                    queue.Enqueue((child, DateTime.UtcNow.AddMilliseconds(delay), childInfo));
                continue;
            }

            child.Execute(state, childInfo);
            FollowChannelEnd(state, child, childInfo, queue, enqueueDelayed);
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

    private static void DoPlotEnd(PlotState state)
    {
        state.Caster?.BroadcastPacket(new SCPlotEndedPacket(state.ActiveSkill.TlId), true);
        EndPlotChannel(state);

        state.Caster?.Cooldowns.AddCooldown(state.ActiveSkill.Template.Id, (uint)state.ActiveSkill.Template.CooldownTime);

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
        if (state.Caster?.ActivePlotState == state)
            state.Caster.ActivePlotState = null;
    }
}
