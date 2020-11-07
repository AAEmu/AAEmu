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
            _log.Trace("Executing plot tree with ID {0}", PlotId);
            var queue = new Queue<(PlotNode node, DateTime timestamp)>();
            var executeQueue = new Queue<PlotNode>();
            
            queue.Enqueue((RootNode, DateTime.Now));
            (PlotNode node, DateTime timestamp) item;
            while (queue.Count > 0)
            {
                item = queue.Dequeue();
                var now = DateTime.Now;
                var node = item.node;

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
                    FlushExecutionQueue(executeQueue);
                }

                if (executeQueue.Count > 0)
                    await Task.Delay(5);
            }

            FlushExecutionQueue(executeQueue);
            
            //DoPlotEnd();
            _log.Trace("Tree with ID {0} has finished executing", PlotId);
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
