using AAEmu.Game.Models.Game.AI.v2.Controls;

namespace AAEmu.Game.Core.Managers;

public interface IAiPathsManager : ILoadable
{
    List<AiPathPoint> LoadAiPathPoints(string aiPathFileName);
}
