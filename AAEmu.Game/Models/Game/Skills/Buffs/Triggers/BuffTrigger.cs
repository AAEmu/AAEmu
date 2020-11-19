using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers
{
    abstract class BuffTrigger
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        protected Effect _owner;
        public BuffTriggerTemplate Template { get; set; }
        public abstract void Execute(object sender, EventArgs args);

        public BuffTrigger(Effect owner, BuffTriggerTemplate template)
        {
            _owner = owner;
            Template = template;
        }
    }
}
