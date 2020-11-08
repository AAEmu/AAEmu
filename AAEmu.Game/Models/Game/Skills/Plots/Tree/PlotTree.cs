using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
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

        public async Task Execute()
        {
            _log.Debug("Executing plot tree with ID {0}", PlotId);
            try
            {
                var stopWatch = new Stopwatch();

                var queue = new Queue<(PlotNode node, DateTime timestamp)>();
                var executeQueue = new Queue<PlotNode>();

                queue.Enqueue((RootNode, DateTime.Now));
                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();
                    var now = DateTime.Now;
                    var node = item.node;

                    if (now >= item.timestamp)
                    {
                        if (node.CheckConditions())
                        {
                            stopWatch.Stop();
                            _log.Debug($"PlotEvent{node.Event.Id} Queued at Delta: {stopWatch.ElapsedMilliseconds}");
                            stopWatch.Start();
                            node.Children.ForEach(o => queue.Enqueue((o, now.AddMilliseconds(o.ComputeDelayMs()))));
                            executeQueue.Enqueue(node);
                        }
                    }
                    else
                    {
                        queue.Enqueue((node, item.timestamp));
                        FlushExecutionQueue(executeQueue);
                    }

                    if (queue.Count > 0)
                    {
                        int delay = (int)queue.Min(o => (o.timestamp - DateTime.Now).TotalMilliseconds);
                        await Task.Delay(Math.Max(delay, 0));
                    }
                }

                FlushExecutionQueue(executeQueue);
            } catch (Exception e)
            {
                _log.Debug($"Plot Error: {e.Message}");
            }
            
            //DoPlotEnd();
            _log.Debug("Tree with ID {0} has finished executing", PlotId);
        }

        private void FlushExecutionQueue(Queue<PlotNode> executeQueue)
        {
            PlotNode execNode;
            while ((execNode = executeQueue.Dequeue()) != null)
            {
                execNode.Execute();
            }
        }
    }
}
