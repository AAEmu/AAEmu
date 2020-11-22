using System;
using System.Collections.Generic;
using System.Text;

namespace AAEmu.Game.Models.Game.Skills.Buffs
{
    public class BuffEvents
    {
        public EventHandler<OnBuffStartedArgs> OnBuffStarted = delegate { };
        public EventHandler<OnDispelledArgs> OnDispelled = delegate { };
        public EventHandler<OnTimeoutArgs> OnTimeout = delegate { };
    }

    public class OnBuffStartedArgs : EventArgs
    {

    }

    public class OnDispelledArgs : EventArgs
    {

    }

    public class OnTimeoutArgs : EventArgs
    {

    }
}
