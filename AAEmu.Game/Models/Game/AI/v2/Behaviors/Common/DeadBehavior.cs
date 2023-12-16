using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class DeadBehavior : Behavior
{
    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnDeath(this, new OnDeathArgs { Killer = npc, Victim = npc });
        }
    }

    public override void Tick(TimeSpan delta)
    {
        if (Ai.Owner.Hp == 0)
        {
            Ai.AlreadyTargetted = false;
        }
    }

    public override void Exit()
    {
    }
}
