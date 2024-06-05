using System;

using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class AlertBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        Ai.Owner.InterruptSkills();

        CheckPipeName();
        Ai.Owner.StopMovement();

        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.InAlert(this, new InAlertArgs { Npc = npc });
        }

        _pipeName = "phase_dragon_ground";
        _enter = true;
    }

    public override void Tick(TimeSpan delta)
    {
        if (!_enter)
            return; // not initialized yet Enter()

        if (!SetTarget() || ShouldReturn) // проверим, что таблица abuser не пустая и назначим текущую цель
        {
            Ai.GoToReturn();
            return;
        }
    }

    public override void Exit()
    {
        _enter = false;
    }
}
