using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class DespawningBehavior : Behavior
{
    public override void Enter()
    {
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnDespawn(this, new OnDespawnArgs { Npc = npc });
        }
    }

    public override void Tick(TimeSpan delta)
    {
    }

    public override void Exit()
    {
    }
}
