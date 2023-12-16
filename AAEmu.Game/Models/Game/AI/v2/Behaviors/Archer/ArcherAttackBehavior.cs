using System;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Archer;

public class ArcherAttackBehavior : BaseCombatBehavior
{
    public override void Enter()
    {
        Ai.Owner.CurrentGameStance = GameStanceType.Combat;
        if (Ai.Owner is { } npc)
        {
            npc.Events.OnCombatStarted(this, new OnCombatStartedArgs { Owner = npc, Target = npc});
        }
    }

    public override void Tick(TimeSpan delta)
    {
        if (!UpdateTarget() || ShouldReturn)
        {
            Ai.GoToReturn();
            return;
        }
    }

    public override void Exit()
    {
    }
}
