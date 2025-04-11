using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree;

public class PlotTree
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public uint PlotId { get; set; }

    public PlotNode RootNode { get; set; }

    public PlotTree(uint plotId)
    {
        PlotId = plotId;
    }

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
                    HandleChannelingFinish(node, state, queue, item);
                    lastEvent = 0;
                    continue;
                }
                if (state.CancellationRequested())
                {
                    if (state.IsCasting)
                    {
                        state.Caster.BroadcastPacket(
                            new SCPlotCastingStoppedPacket(state.ActiveSkill.TlId, 0, lastEvent),
                            true
                        );
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

                    //Check if we hit max tickets
                    if (state.Tickets[node.Event.Id] > node.Event.Tickets && node.Event.Tickets > 1)
                    {
                        continue;
                    }

                    item.targetInfo.UpdateTargetInfo(node.Event, state);

                    if (item.targetInfo.Target == null)
                        continue;

                    var condition = node.CheckConditions(state, item.targetInfo);

                    if (condition)
                    {
                        executeQueue.Enqueue((node, item.targetInfo));
                    }

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
    private void HandleChannelingFinish(PlotNode node, PlotState state, Queue<(PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo)> queue, (PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo) item)
    {
        if (node == null || state == null || queue == null || item.targetInfo == null)
        {
            Logger.Error($"Plot {PlotId}: Invalid arguments passed to HandleChannelingFinish.");
            return;
        }

        // Stop all active channeling or casting nodes
        EndPlotChannel(state);

        // Check if ParentNextEvent is null
        if (node.ParentNextEvent == null)
        {
            return;
        }

        // Determine the correct node to trigger
        if (node.ParentNextEvent.Channeling)
        {

            // Reset channeling state
            state.PermitChanneling();

            // Execute the correct node fully before moving to children
            node.Execute(state, item.targetInfo);

            // Use a HashSet to track unique node IDs already in the queue
            var queuedNodeIds = new HashSet<uint>(queue.Select(q => q.node.Event.Id));

            // Only enqueue children after the current node is executed
            foreach (var child in node.Children ?? Enumerable.Empty<PlotNode>())
            {
                if (child == null || child.Event == null)
                {
                    Logger.Warn($"Plot {PlotId}: Skipping null child or child with null Event.");
                    continue;
                }

                // Check if the child node's Event.Id is already in the queue
                if (!queuedNodeIds.Contains(child.Event.Id))
                {
                    if (child.ParentNextEvent?.PerTarget ?? false)
                    {
                        foreach (var target in item.targetInfo.EffectedTargets)
                        {

                            var targetInfo = new PlotTargetInfo(item.targetInfo.Source, target);
                            queue.Enqueue((child, DateTime.UtcNow, targetInfo));
                            queuedNodeIds.Add(child.Event.Id); // Mark this node as queued
                        }
                    }
                    else
                    {
                        var targetInfo = new PlotTargetInfo(item.targetInfo.Source, item.targetInfo.Target);
                        queue.Enqueue((child, DateTime.UtcNow, targetInfo));
                        queuedNodeIds.Add(child.Event.Id); // Mark this node as queued
                    }

                }
                else
                {
                    //Logger.Debug($"Plot {PlotId}: Child node {child.Event.Id} is already in the queue. Skipping.");
                }
            }
        }
        else
        {
            //Logger.Debug($"Plot {PlotId}: No channeling node to transition to.");
        }
    }
    private static void FlushExecutionQueue(Queue<(PlotNode node, PlotTargetInfo targetInfo)> executeQueue, PlotState state)
    {


        var packets = new CompressedGamePackets();
        while (executeQueue.Count > 0)
        {
            var item = executeQueue.Dequeue();
            item.node.Execute(state, item.targetInfo, packets);
        }

        if (packets.Packets.Count > 0)
            state.Caster.BroadcastPacket(packets, true);
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

        SkillTlIdManager.ReleaseId(state.ActiveSkill.TlId);
        state.ActiveSkill.TlId = 0;

        state.Caster?.OnSkillEnd(state.ActiveSkill);
        state.ActiveSkill.Callback?.Invoke();
        if (state.Caster?.ActivePlotState == state)
            state.Caster.ActivePlotState = null;
    }
}
