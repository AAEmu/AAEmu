using AAEmu.Game.Models.Game.AI.v2.Framework;

namespace AAEmu.Game.Core.Managers;

public interface IAIManager : IInitializable
{
    void AddAi(NpcAi ai);
    void Tick(TimeSpan delta);
    void Stop();
}
