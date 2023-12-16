using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class RunCommandSetBehavior : Behavior
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
