using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Units
{
    public class UnitEvents
    {
        //public EventHandler<OnAttackArgs> OnAttack = delegate { };
        //public EventHandler<OnAttackedArgs> OnAttacked = delegate { };
        public EventHandler<OnDamageArgs> OnDamage = delegate { };
        public EventHandler<OnDamagedArgs> OnDamaged = delegate { };
        public EventHandler<OnDispelledArgs> OnDispelled = delegate { };
        //public EventHandler<OnTimeoutArgs> OnTimeout = delegate { };
        //public EventHandler<OnDamagedMeleeArgs> OnDamagedMelee = delegate { };
        //public EventHandler<OnDamagedRangedArgs> OnDamagedRanged = delegate { };
        //public EventHandler<OnDamagedSpellArgs> OnDamagedSpell = delegate { };
        //public EventHandler<OnDamagedSiegeArgs> OnDamagedSiege = delegate { };
        //public EventHandler<OnLandingArgs> OnLanding = delegate { };
        //public EventHandler<OnStartedArgs> OnStarted = delegate { }; // I think this belongs part of effect
        public EventHandler<OnMovementArgs> OnMovement = delegate { };
        public EventHandler<OnChannelingCancelArgs> OnChannelingCancel = delegate { };
        //public EventHandler<OnRemoveOnDamagedArgs> OnRemoveOnDamaged = delegate { };
        public EventHandler<OnDeathArgs> OnDeath = delegate { };
        public EventHandler<OnUnmountArgs> OnUnmount = delegate { };
        public EventHandler<OnKillArgs> OnKill = delegate { };
        //public EventHandler<OnDamagedCollisionArgs> OnDamagedCollision = delegate { };//I think for ships
        //public EventHandler<OnImmortalityArgs> OnImmortality = delegate { };
        //public EventHandler<OnTimeArgs> OnTime = delegate { }; //Event for effect?
        //OnKillAny == OnKill? Add it if needed
    }

    public class OnAttackArgs : EventArgs
    {

    }

    public class OnAttackedArgs : EventArgs
    {

    }

    public class OnDamageArgs : EventArgs
    {

    }

    public class OnDamagedArgs : EventArgs
    {

    }

    public class OnDispelledArgs : EventArgs
    {

    }

    public class OnTimeoutArgs : EventArgs
    {

    }

    public class OnDamagedMeleeArgs : EventArgs
    {

    }

    public class OnDamagedRangedArgs : EventArgs
    {

    }

    public class OnDamagedSpellArgs : EventArgs
    {

    }

    public class OnDamagedSiegeArgs : EventArgs
    {

    }

    public class OnLandingArgs : EventArgs
    {

    }

    public class OnStartedArgs : EventArgs
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
}

