namespace AAEmu.Game.Models.Game.AI.v2
{
    // TODO: Fill
    public enum TransitionEvent
    {
        OnAggroTargetChanged,
        OnTalk,
        OnReturnToTalkPos,
        OnNoAggroTarget,
        ReturnToIdlePos,
    }
    
    public class Transition
    {
        public TransitionEvent On { get; set; }
        // public Behavior Target { get; set; }
        public BehaviorKind Kind { get; set; }

        public Transition(TransitionEvent on, BehaviorKind target)
        {
            On = on;
            Kind = target;
        }
    }
}
