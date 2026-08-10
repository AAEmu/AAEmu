using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Hero;

/// <summary>Watches for the hero season rolling from one scheduled phase into the next.</summary>
public class HeroPhaseTickTask : Task
{
    public override void Execute()
    {
        HeroElectionManager.Instance.Tick();
    }
}
