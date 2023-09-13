using AAEmu.Game.Models.Game.AI.v2.Behaviors.Common;
using AAEmu.Game.Models.Game.AI.v2.Framework;

namespace AAEmu.Game.Models.Game.AI.v2.AiCharacters
{
    public class TowerDefenseAttackerAiCharacter : NpcAi
    {
        protected override void Build()
        {
            AddBehavior(BehaviorKind.Spawning, new SpawningBehavior());

            AddBehavior(BehaviorKind.HoldPosition, new HoldPositionBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.Attack)
                .AddTransition(TransitionEvent.ReturnToIdlePos, BehaviorKind.ReturnState)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.RunCommandSet, new RunCommandSetBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.Attack)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.Talk, new TalkBehavior())
                .AddTransition(TransitionEvent.OnReturnToTalkPos, BehaviorKind.ReturnState)
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.Attack);

            AddBehavior(BehaviorKind.Alert, new AlertBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.Attack);

            AddBehavior(BehaviorKind.Attack, new AttackBehavior())
                .AddTransition(TransitionEvent.OnNoAggroTarget, BehaviorKind.ReturnState);

            AddBehavior(BehaviorKind.FollowPath, new FollowPathBehavior())
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.FollowUnit, new FollowUnitBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.Attack)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.ReturnState, new ReturnStateBehavior());
            AddBehavior(BehaviorKind.Dead, new DeadBehavior());
            AddBehavior(BehaviorKind.Despawning, new DespawningBehavior());
        }

        public override void GoToIdle()
        {
            SetCurrentBehavior(BehaviorKind.HoldPosition);
        }

        public override void GoToCombat()
        {
            SetCurrentBehavior(BehaviorKind.Attack);
        }
    }
}