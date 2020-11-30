using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree
{
    public class PlotTree
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        public uint PlotId { get; set; }
        
        public PlotNode RootNode { get; set; }

        public PlotTree(uint plotId)
        {
            PlotId = plotId;
        }

        public async Task Execute(PlotState state)
        {
            var treeWatch = new Stopwatch();
            treeWatch.Start();
            _log.Debug("Executing plot tree with ID {0}", PlotId);
            try
            {
                var stopWatch = new Stopwatch();
                stopWatch.Start();

                var queue = new Queue<(PlotNode node, DateTime timestamp, PlotTargetInfo targetInfo)>();
                var executeQueue = new Queue<(PlotNode node, PlotTargetInfo targetInfo)>();

                queue.Enqueue((RootNode, DateTime.Now, new PlotTargetInfo(state)));

                while (queue.Count > 0)
                {
                    var nodewatch = new Stopwatch();
                    nodewatch.Start();
                    if (state.CancellationRequested())
                    {
                        if (state.IsCasting)
                        {
                            state.Caster.BroadcastPacket(
                                new SCPlotCastingStoppedPacket(state.ActiveSkill.TlId, 0, 1),
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
                    var item = queue.Dequeue();
                    var now = DateTime.Now;
                    var node = item.node;

                    if (now >= item.timestamp)
                    {
                        if (state.Tickets.ContainsKey(node.Event.Id))
                            state.Tickets[node.Event.Id]++;
                        else
                            state.Tickets.TryAdd(node.Event.Id, 1);

                        //Check if we hit max tickets
                        if (state.Tickets[node.Event.Id] > node.Event.Tickets
                            && node.Event.Tickets > 1)
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
                                if (child?.ParentNextEvent?.PerTarget ?? false)
                                {
                                    foreach(var target in item.targetInfo.EffectedTargets)
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
                        int delay = (int)queue.Min(o => (o.timestamp - DateTime.Now).TotalMilliseconds);
                        delay = Math.Max(delay, 0);

                        //await Task.Delay(delay).ConfigureAwait(false);
                        if (delay > 0)
                            await Task.Delay(15).ConfigureAwait(false);
                        
                    }

                    if (nodewatch.ElapsedMilliseconds > 100)
                        _log.Trace($"Event:{node.Event.Id} Took {nodewatch.ElapsedMilliseconds} to finish.");
                }

                FlushExecutionQueue(executeQueue, state);
            } catch (Exception e)
            {
                _log.Error($"Main Loop Error: {e.Message}\n {e.StackTrace}");
            }
            
            DoPlotEnd(state);
            _log.Trace("Tree with ID {0} has finished executing took {1}ms", PlotId, treeWatch.ElapsedMilliseconds);
        }
        
        private void FlushExecutionQueue(Queue<(PlotNode node, PlotTargetInfo targetInfo)> executeQueue, PlotState state)
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

        private void EndPlotChannel(PlotState state)
        {
            foreach(var pair in state.ChanneledBuffs)
            {
                pair.unit.Buffs.RemoveBuff(pair.buffId);
            }
        }

        private void DoPlotEnd(PlotState state)
        {
            state.Caster?.BroadcastPacket(new SCPlotEndedPacket(state.ActiveSkill.TlId), true);
            EndPlotChannel(state);

            if (state.Caster is Character character && character.IgnoreSkillCooldowns)
                character.ResetSkillCooldown(state.ActiveSkill.Template.Id, false);

            //Maybe always do thsi on end of plot?
            //Should we check if it was a channeled skill?
            if (state.CancellationRequested())
                state.Caster.Events.OnChannelingCancel(state.ActiveSkill, new OnChannelingCancelArgs { });

            SkillManager.Instance.ReleaseId(state.ActiveSkill.TlId);
        }
    }
}
