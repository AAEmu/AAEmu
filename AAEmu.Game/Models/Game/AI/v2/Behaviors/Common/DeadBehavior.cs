using System;

using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class DeadBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();
        Ai.Owner.StopMovement();
        Ai.Owner.ClearAllAggro();
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnDeath(this, new OnDeathArgs { Killer = npc, Victim = npc });
        }
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (Ai.Owner.Hp == 0)
        {
            Ai.AlreadyTargeted = false;
        }
    }

    public override void Exit()
    {
        _enter = false;
    }
}
