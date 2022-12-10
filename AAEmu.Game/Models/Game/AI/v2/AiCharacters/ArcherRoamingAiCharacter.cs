using AAEmu.Game.Models.Game.AI.v2.Behaviors;
using AAEmu.Game.Models.Game.AI.v2.Behaviors.Archer;

namespace AAEmu.Game.Models.Game.AI.v2.AiCharacters
{
    public class ArcherRoamingAiCharacter : NpcAi
    {
        protected override void Build()
        {
            AddBehavior(BehaviorKind.Spawning, new SpawningBehavior());
            
            AddBehavior(BehaviorKind.Roaming, new RoamingBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.ArcherAttack)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.RunCommandSet, new RunCommandSetBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.ArcherAttack)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.Talk, new TalkBehavior())
                .AddTransition(TransitionEvent.OnReturnToTalkPos, BehaviorKind.ReturnState)
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.ArcherAttack);

            AddBehavior(BehaviorKind.Alert, new AlertBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.ArcherAttack);

            AddBehavior(BehaviorKind.ArcherAttack, new ArcherAttackBehavior())
                .AddTransition(TransitionEvent.OnNoAggroTarget, BehaviorKind.ReturnState);

            AddBehavior(BehaviorKind.FollowPath, new FollowPathBehavior())
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.FollowUnit, new FollowUnitBehavior())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.ArcherAttack)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(BehaviorKind.ReturnState, new ReturnStateBehavior());
            AddBehavior(BehaviorKind.Dead, new DeadBehavior());
            AddBehavior(BehaviorKind.Despawning, new DespawningBehavior());
        }

        public override void GoToIdle()
        {
            SetCurrentBehavior(BehaviorKind.Roaming);
        }

        public override void GoToCombat()
        {
            SetCurrentBehavior(BehaviorKind.ArcherAttack);
        }
    }
}
