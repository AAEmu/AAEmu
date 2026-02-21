using AAEmu.Game.Models.Game.Skills.Plots;

namespace AAEmu.Game.Core.Managers;

public interface IPlotManager
{
    Plot GetPlot(uint id);
    PlotEventTemplate GetEventByPlotId(uint plotId);
    void Load();
}
