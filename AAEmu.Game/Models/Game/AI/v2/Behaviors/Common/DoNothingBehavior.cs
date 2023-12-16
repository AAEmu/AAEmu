using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Models;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class DoNothingBehavior : Behavior

{
    public override void Enter()
    {
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
    }

    public override void Tick(TimeSpan delta)
    {
    }

    public override void Exit()
    {
    }
}
