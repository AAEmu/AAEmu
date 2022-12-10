using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI.Framework
{
    /// <summary>
    /// AI for Units specifically. Handles being in world, being able to take damage etc.
    /// </summary>
    public abstract class AbstractUnitAI : AbstractAI
    {
        public virtual void OnSkillEnd(Skill skill) {}
        
        // TODO : Implement all of these. They are taken from template.lua
        
        // called after finishing any AI action for which this agent was "the user"
        // public abstract void OnActionDone();

        // called when the AI sees a living enemy
        public virtual void OnEnemySeen(Unit enemy) {}

        // called when AI gets at close distance to an enemy
        // public abstract void OnCloseContact();

        // called when the AI can no longer see its enemy, but remembers where it saw it last
        // public abstract void OnEnemyMemory();

        // called when the AI stops having an attention target
        // public abstract void OnNoTarget();

        // called when the AI hears an interesting sound
        // public abstract void OnInterestingSoundHeard();

        // called when the AI hears a threatening sound
        // public abstract void OnThreateningSoundHeard();

        // called when the AI sees an object registered for this kind of signal
        // public abstract void OnObjectSeen();
        
        // called when the AI couldn't find a formation point
        // public abstract void OnNoFormationPoint();
        
        // called when AI is damaged by another friendly/unknown AI
        // public abstract void OnDamage();
        
        // called when AI is damaged by an enemy AI
        public virtual void OnEnemyDamage(Unit enemy){}

        // called when a member of same species dies nearby
        // public abstract void OnSomebodyDied();
        
        // called when the AI's group leader dies
        // public abstract void OnLeaderDied();
        
        // called when the attention target is too close for the current weapon range
        // public abstract void OnTargetTooClose();

        //  called when the attention target is too close for the current weapon range
        // public abstract void OnTargetTooFar();
        
        // player is staying close to the ai since<entity.Properties.awarenessOfPlayer> seconds
        // public abstract void OnPlayerSticking();

        // player has just stopped staying close to the AI
        // public abstract void OnPlayerGoingAway();
    }
}
