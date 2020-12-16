using System;

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
    public abstract class Behavior
    {
        private NpcAi Ai { get; set; }

        public abstract void Enter();
        public abstract void Tick(TimeSpan delta);
        public abstract void Exit();
        
        
        public Behavior AddTransition(TransitionEvent on, BehaviorKind kind)
        {
            return AddTransition(new Transition(on, Ai.GetBehavior(kind)));
        }
        
        public Behavior AddTransition(Transition transition)
        {
            return Ai.AddTransition(this, transition);
        }
    }
}
