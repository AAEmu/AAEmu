﻿using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Buffs.Triggers
{
    public class BuffTrigger
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        protected Buff _buff;
        protected readonly BaseUnit _owner;
        public BuffTriggerTemplate Template { get; set; }
        public virtual void Execute(object sender, EventArgs eventArgs)
        {
            var args = eventArgs as OnTimeoutArgs;
            _log.Trace("Buff[{0}] {1} executed. Applying {2}[{3}]!", _buff?.Template?.BuffId, this.GetType().Name, Template.Effect.GetType().Name, Template.Effect.Id);
            //Template.Effect.Apply()

            if (!(_owner is Unit owner))
            {
                _log.Warn("Owner is not a Unit");
                return;
            }

            var target = _buff.Owner;
            var source = (Unit)_buff.Owner;

            if (Template.UseOriginalSource)
            {
                source = _buff.Caster;
            }

            if (Template.EffectOnSource)
            {
                target = source;
            }

            if (Template.TargetBuffTagId != 0)
            {
                if (!target.Buffs.CheckBuffTag(Template.TargetBuffTagId))
                    return;
            }
            if (Template.TargetNoBuffTagId != 0)
            {
                if (target.Buffs.CheckBuffTag(Template.TargetNoBuffTagId))
                    return;
            }

            Template.Effect.Apply(source, new SkillCasterUnit(_owner.ObjId), target, new SkillCastUnitTarget(target.ObjId), new CastBuff(_buff),
                new EffectSource(_buff?.Skill), // TODO : EffectSource Type trigger 
                null, DateTime.Now);
        }

        public BuffTrigger(Buff buff, BuffTriggerTemplate template)
        {
            _buff = buff;
            _owner = _buff.Owner;
            Template = template;
        }
    }
}
