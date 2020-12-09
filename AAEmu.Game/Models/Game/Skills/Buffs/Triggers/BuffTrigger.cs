using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers
{
    abstract class BuffTrigger
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        protected Buff _buff;
        protected readonly BaseUnit _owner;
        public BuffTriggerTemplate Template { get; set; }
        public abstract void Execute(object sender, EventArgs args);

        public BuffTrigger(Buff buff, BuffTriggerTemplate template)
        {
            _buff = buff;
            _owner = _buff.Owner;
            Template = template;
        }
    }
}
