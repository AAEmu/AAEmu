using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Flytrap;
using AAEmu.Game.Models.Game.AI.v2.Framework;

namespace AAEmu.Game.Models.Game.AI.v2.AiCharacters;

public class FlytrapAiCharacter : NpcAi
{
    protected override void Build()
    {
        AddBehavior(BehaviorKind.Spawning, new SpawningBehavior());

        AddBehavior(BehaviorKind.HoldPosition, new HoldPositionBehavior())
            .SetDefaultBehavior()
            .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.FlytrapAttack)
            .AddTransition(TransitionEvent.ReturnToIdlePos, BehaviorKind.ReturnState)
            .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

        AddBehavior(BehaviorKind.RunCommandSet, new RunCommandSetBehavior())
            .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.FlytrapAttack)
            .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

        AddBehavior(BehaviorKind.Talk, new TalkBehavior())
            .AddTransition(TransitionEvent.OnReturnToTalkPos, BehaviorKind.ReturnState)
            .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.FlytrapAttack);

        AddBehavior(BehaviorKind.FlytrapAlert, new FlytrapAlertBehavior())
            .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.FlytrapAttack);

        AddBehavior(BehaviorKind.FlytrapAttack, new FlytrapAttackBehavior());

        AddBehavior(BehaviorKind.ReturnState, new ReturnStateBehavior());
        AddBehavior(BehaviorKind.Dead, new DeadBehavior());
        AddBehavior(BehaviorKind.Despawning, new DespawningBehavior());
        AddBehavior(BehaviorKind.Idle, new IdleBehavior());
    }

    // public override void GoToIdle()
    // {
    //     SetCurrentBehavior(BehaviorKind.HoldPosition);
    // }

    public override void GoToAlert()
    {
        SetCurrentBehavior(BehaviorKind.FlytrapAlert);
    }

    public override void GoToCombat()
    {
        SetCurrentBehavior(BehaviorKind.FlytrapAttack);
    }
}
