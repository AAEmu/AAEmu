using AAEmu.Game.Models.Game.AI.v2.Behaviors;

namespace AAEmu.Game.Models.Game.AI.v2.AiCharacters
{
    /// <summary>
    /// Named as such because of game files
    /// </summary>
    public class AlmightyNpcAiCharacter : NpcAi
    {
        public override void Build()
        {
            AddBehavior(new Idle())
                .AddTransition(TransitionEvent.OnAggroTargetChanged, BehaviorKind.AlmightyAttack)
                .AddTransition(TransitionEvent.ReturnToIdlePos, BehaviorKind.ReturnState)
                .AddTransition(TransitionEvent.OnTalk, BehaviorKind.Talk);

            AddBehavior(new AlmightyAttack())
                .AddTransition(TransitionEvent.OnNoAggroTarget, BehaviorKind.ReturnState);
        }
    }
}
