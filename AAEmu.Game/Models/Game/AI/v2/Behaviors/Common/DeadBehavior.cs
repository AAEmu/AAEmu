using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Movements;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;

public class DeadBehavior : BaseCombatBehavior
{
    private bool _enter;

    public override void Enter()
    {
        // NOTE: Do NOT call InterruptSkills() here. Unit.DoDie() already interrupts
        // pre-death skills, and by this point OnDeath skills (e.g. plot-based loot
        // spawning) have started asynchronously. Calling InterruptSkills() here would
        // cancel those OnDeath plots prematurely.
        // Also do NOT fire Events.OnDeath() — it was already fired in Unit.DoDie()
        // with the correct killer info. The duplicate call here passed the NPC as its
        // own killer and could interfere with cooldown-gated OnDeath skills.
        Ai.Owner.StopMovement();
        Ai.Owner.ClearAllAggro();
        Ai.Owner.CurrentAlertness = MoveTypeAlertness.Idle;
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
