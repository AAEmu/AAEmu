namespace AAEmu.Game.Models.Game.AI.v2.Framework;

public class Transition(TransitionEvent on, BehaviorKind target)
{
    public TransitionEvent On { get; set; } = on;

    // public Behavior Target { get; set; }
    public BehaviorKind Kind { get; set; } = target;
}
