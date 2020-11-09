using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AAEmu.Game.Core.Packets.G2C;
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

                        if(node.Event.Id == 12104)
                            _log.Debug($"Processing Node:{node.Event.Id}({state.Tickets[node.Event.Id]}) at Delta: {stopWatch.ElapsedMilliseconds}");
                        item.targetInfo.UpdateTargetInfo(node.Event, state);

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
                                        item.targetInfo.Target = target;
                                        var targetInfo = new PlotTargetInfo(item.targetInfo);
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
                                    var targetInfo = new PlotTargetInfo(item.targetInfo);
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
                        _log.Debug($"BEFORE DELAY - Event:{node.Event.Id} Took {nodewatch.ElapsedMilliseconds} to finish.");
                        int delay = (int)queue.Min(o => (o.timestamp - DateTime.Now).TotalMilliseconds);
                        delay = Math.Max(delay, 0);

                        //await Task.Delay(delay).ConfigureAwait(false);
                        if (delay > 0)
                            await Task.Delay(15).ConfigureAwait(false);
                        
                    }

                    //if (nodewatch.ElapsedMilliseconds > 10)
                        _log.Debug($"Event:{node.Event.Id} Took {nodewatch.ElapsedMilliseconds} to finish.");
                }

                FlushExecutionQueue(executeQueue, state);
            } catch (Exception e)
            {
                _log.Debug($"Plot Error: {e.Message}\n Line: {e.StackTrace}");
            }
            
            DoPlotEnd(state);
            _log.Debug("Tree with ID {0} has finished executing took {1}ms", PlotId, treeWatch.ElapsedMilliseconds);
        }
        
        private void FlushExecutionQueue(Queue<(PlotNode node, PlotTargetInfo targetInfo)> executeQueue, PlotState state)
        { 
            while (executeQueue.Count > 0)
            {
                var item = executeQueue.Dequeue();
                item.node.Execute(state, item.targetInfo);
            }
        }

        private void DoPlotEnd(PlotState state)
        {
            state.Caster?.BroadcastPacket(new SCPlotEndedPacket(state.ActiveSkill.TlId), true);
        }
    }
}
