using System;
using System.Collections.Generic;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots.Tree
{
    public class PlotNode
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        
        // Tree
        public PlotTree Tree;
        public List<PlotNode> Children;
        // Plots
        public PlotEventTemplate Event;
        public PlotNextEvent ParentNextEvent;
        

        public PlotNode()
        {
            Children = new List<PlotNode>();
        }

        public int ComputeDelayMs()
        {
            return ParentNextEvent.Delay;
        }
        
        public bool CheckConditions(PlotState state, PlotTargetInfo targetInfo)
        {
            return true;
        }

        public void Execute()
        {
            _log.Debug("Executing plot node with id {0}", Event.Id);
        }
    }
}
