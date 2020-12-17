using System;

namespace AAEmu.Game.Models.Game.AI.v2
{
    public enum BehaviorKind
    {
        // Common
        Alert,
        AlmightyAttack,
        Attack,
        Dead,
        Despawning,
        DoNothing,
        Dummy,
        FollowPath,
        FollowUnit,
        HoldPosition,
        Idle,
        ReturnState,
        Roaming,
        RunCommandSet,
        Spawning,
        Talk,
        
        // Archer
        ArcherAttack,
        
        // BigMonster
        BigMonsterAttack,
        
        // Flytrap
        FlytrapAlert,
        FlytrapAttack,
        
        // WildBoar
        WildBoatAttack
    }
    
    /// <summary>
    /// Represents an AI state. Called as such because of naming in the game's files.
    /// </summary>
    public abstract class Behavior
    {
        protected NpcAi Ai { get; set; }

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
