namespace AAEmu.Game.Models.Game.AI.v2
{
    public enum BehaviorKind
    {
        Spawning,
        Idle,
        RunCommandSet,
        Talk,
        Alert,
        AlmightyAttack,
        FollowPath,
        FollowUnit,
        ReturnState,
        Dead,
        Despawning
    }
    
    /// <summary>
    /// Represents an AI state. Called as such because of naming in the game's files.
    /// </summary>
    public class Behavior
    {
        private NpcAi Ai { get; set; }

        public Behavior AddTransition(TransitionEvent on, BehaviorKind kind)
        {
            return AddTransition(new Transition(on, Ai.GetBehavior(kind)));
        }
        
        public Behavior AddTransition(Transition transition)
        {
            Ai.AddTransition(this, transition);
            return this;
        }
    }
}
