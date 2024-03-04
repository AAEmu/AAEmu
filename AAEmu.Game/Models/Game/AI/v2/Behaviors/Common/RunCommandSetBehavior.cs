using System;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RunCommandSetBehavior : BaseCombatBehavior
{
    public override void Enter()
    {
    }

    public override void Tick(TimeSpan delta)
    {
        // TODO: Proper code
        Ai.GoToIdle();
    }

    public override void Exit()
    {
    }
}
