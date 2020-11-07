using System;
using System.Collections.Generic;
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
            _log.Debug("Executing tree with ID {0}", PlotId);
            var queue = new Queue<(PlotNode node, DateTime timestamp)>();
            var executeQueue = new Queue<PlotNode>();
            
            queue.Enqueue((RootNode, DateTime.Now));
            (PlotNode node, DateTime timestamp) item;
            while ((item = queue.Dequeue()).node != null)
            {
                var now = DateTime.Now;
                var node = item.node;
                // var elapsedTime = (now - item.timestamp).TotalMilliseconds;

                if (now >= item.timestamp)
                {
                    if (node.CheckConditions())
                    {
                        node.Children.ForEach(o => queue.Enqueue((o, now.AddMilliseconds(o.ComputeDelayMs()))));
                        executeQueue.Enqueue(node);
                    }
                }
                else
                {
                    queue.Enqueue((node, item.timestamp));
                    foreach (var plotNode in executeQueue)
                    {
                        plotNode.Execute();
                    }
                }

                await Task.Delay(5);
            }
            
            //DoPlotEnd();
            _log.Debug("Tree with ID {0} has finished executing", PlotId);
        }
    }
}
