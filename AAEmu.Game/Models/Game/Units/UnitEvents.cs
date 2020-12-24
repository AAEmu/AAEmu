using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Units
{
    public class UnitEvents
    {
        /********************************************************
         *  Please dont uncomment unless you implement these!   *
         *           Commented = Not Invoked!!!                 *
         ********************************************************/

        public EventHandler<OnAttackArgs> OnAttack = delegate { }; //Double check this one
        public EventHandler<OnAttackedArgs> OnAttacked = delegate { }; //Double check this one
        public EventHandler<OnDamageArgs> OnDamage = delegate { };
        public EventHandler<OnDamagedArgs> OnDamaged = delegate { };
        //public EventHandler<OnTimeoutArgs> OnTimeout = delegate { }; //When player disconnects? Buff runs out? idk
        public EventHandler<OnDamagedArgs> OnDamagedMelee = delegate { };
        public EventHandler<OnDamagedArgs> OnDamagedRanged = delegate { };
        public EventHandler<OnDamagedArgs> OnDamagedSpell = delegate { };
        public EventHandler<OnDamagedArgs> OnDamagedSiege = delegate { };
        //public EventHandler<OnLandingArgs> OnLanding = delegate { }; //Assume this is for falling?
        //public EventHandler<OnStartedArgs> OnStarted = delegate { }; // I think this belongs part of effect
        public EventHandler<OnMovementArgs> OnMovement = delegate { }; // Only for walking? Or Movement in general?
        public EventHandler<OnChannelingCancelArgs> OnChannelingCancel = delegate { }; //This one might need fixing
        //public EventHandler<OnRemoveOnDamagedArgs> OnRemoveOnDamaged = delegate { }; // Covered by OnDamaged? Maybe?
        public EventHandler<OnDeathArgs> OnDeath = delegate { };
        public EventHandler<OnUnmountArgs> OnUnmount = delegate { };
        public EventHandler<OnKillArgs> OnKill = delegate { };
        //public EventHandler<OnDamagedCollisionArgs> OnDamagedCollision = delegate { };//I think for ships
        //public EventHandler<OnImmortalityArgs> OnImmortality = delegate { }; //When unit goes invuln?
        //public EventHandler<OnTimeArgs> OnTime = delegate { }; //Event for effect?
        //public EventHandler<OnTimeArgs> OnTime = delegate { }; //Add it if needed, but I think OnKill is fine?
        public EventHandler<OnHealedArgs> OnHealed = delegate { };
    }

    public class OnAttackArgs : EventArgs
    {
        public Unit Attacker { get; set; }
    }

    public class OnAttackedArgs : EventArgs
    {

    }

    public class OnDamageArgs : EventArgs
    {
        public Unit Attacker { get; set; }
        public int Amount { get; set; }
    }

    public class OnDamagedArgs : EventArgs
    {
        public Unit Attacker { get; set; }
        public int Amount { get; set; }
    }

    public class OnDamagedMeleeArgs : EventArgs
    {
        public Unit Attacker { get; set; }
        public int Amount { get; set; }
    }

    public class OnDamagedRangedArgs : EventArgs
    {
        public Unit Attacker { get; set; }
        public int Amount { get; set; }
    }

    public class OnDamagedSpellArgs : EventArgs
    {
        public Unit Attacker { get; set; }
        public int Amount { get; set; }
    }

    public class OnDamagedSiegeArgs : EventArgs
    {
        public Unit Attacker { get; set; }
        public int Amount { get; set; }
    }

    public class OnLandingArgs : EventArgs
    {

    }

    public class OnMovementArgs : EventArgs
    {

    }

    public class OnChannelingCancelArgs : EventArgs
    {

    }

    public class OnRemoveOnDamagedArgs : EventArgs
    {

    }

    public class OnDeathArgs : EventArgs
    {
        public Unit Killer { get; set; }
        public Unit Victim { get; set; }
    }

    public class OnUnmountArgs : EventArgs
    {

    }

    public class OnKillArgs : EventArgs
    {
        public Unit target { get; set; }
    }

    public class OnDamagedCollisionArgs : EventArgs
    {

    }

    public class OnImmortalityArgs : EventArgs
    {

    }

    public class OnTimeArgs : EventArgs
    {

    }

    public class OnKillAnyArgs : EventArgs
    {

    }

    public class OnHealedArgs : EventArgs
    {
        public Unit Healer { get; set; }
        public int HealAmount { get; set; }
    }
}

