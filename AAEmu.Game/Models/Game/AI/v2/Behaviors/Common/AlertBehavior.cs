using System;

using AAEmu.Game.Models.Game.AI.v2.Framework;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlertBehavior : Behavior
{
    public override void Enter()
    {
        if (Ai.Owner is { } npc)
        {
            npc.Events.InAlert(this, new InAlertArgs { Npc = npc });
        }
    }

    public override void Tick(TimeSpan delta)
    {
    }

    public override void Exit()
    {
    }
}
