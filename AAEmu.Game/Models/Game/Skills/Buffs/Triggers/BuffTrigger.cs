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
        protected Effect _effect;
        protected readonly BaseUnit _owner;
        public BuffTriggerTemplate Template { get; set; }
        public abstract void Execute(object sender, EventArgs args);

        public BuffTrigger(Effect effect, BuffTriggerTemplate template)
        {
            _effect = effect;
            _owner = _effect.Owner;
            Template = template;
        }
    }
}
