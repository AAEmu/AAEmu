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
        private TransitionEvent On { get; set; }
        private Behavior Target { get; set; }

        public Transition(TransitionEvent on, Behavior target)
        {
            On = on;
            Target = target;
        }
    }
}
