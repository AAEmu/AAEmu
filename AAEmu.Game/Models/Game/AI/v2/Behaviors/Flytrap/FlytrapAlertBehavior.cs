using System;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Flytrap;

public class FlytrapAlertBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.InAlert(this, new InAlertArgs { Npc = npc });
        }
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        CheckAggression();
    }

    public override void Exit()
    {
        _enter = false;
    }
}
