using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.Proxy;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree
{
    public class PlotBuilder
    {

        public static PlotTree BuildTree(uint plotId)
        {
            var tree = new PlotTree(plotId);
            var existingNextEvents = new Dictionary<uint, PlotNode>();

            var firstEvent = PlotManager.Instance.GetEventByPlotId(plotId);
            
            // Create RootNode
            var rootNode = new PlotNode
            {
                Tree = tree,
                Event = firstEvent
            };
            
            tree.RootNode = rootNode;
            // Start the recursion
            BuildChildren(rootNode, existingNextEvents);
            
            return tree;
        }

        private static void BuildChildren(PlotNode parent, Dictionary<uint, PlotNode> existingNextEvents)
        {
            var childNextEvents = parent.Event.NextEvents;

            foreach (var childNextEvent in childNextEvents)
            {
                if (existingNextEvents.ContainsKey(childNextEvent.Id))
                {
                    parent.Children.Add(existingNextEvents[childNextEvent.Id]);
                }
                else
                {
                    var childNode = new PlotNode() {
                        Tree = parent.Tree, 
                        Event = childNextEvent.Event, 
                        ParentNextEvent = childNextEvent
                    };
                    parent.Children.Add(childNode);
                    
                    existingNextEvents.Add(childNextEvent.Id, childNode);

                    BuildChildren(childNode, existingNextEvents);
                }
            }
        }
    }
}
