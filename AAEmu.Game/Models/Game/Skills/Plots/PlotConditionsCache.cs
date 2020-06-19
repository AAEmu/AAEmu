using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotConditionsCache
    {
        //We probably only need to cache bufftags..
        public Dictionary<int, bool> BuffTagCache { get; set; }       

        public PlotConditionsCache()
        {
            BuffTagCache = new Dictionary<int, bool>();
        }
    }
}
