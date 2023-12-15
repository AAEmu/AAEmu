using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class IdleBehavior : Behavior
{
    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.CurrentGameStance = GameStanceType.Relaxed;
        if (Ai.Owner is { } npc)
        {
            npc.Events.InIdle(this, new InIdleArgs { Owner = npc });
        }
    }

    public override void Tick(TimeSpan delta)
    {
        CheckAggression();
    }

    public override void Exit()
    {
    }
}
