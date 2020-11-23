using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.Game.Models.Game.Units
{
    public class CombatBuffs
    {
        private BaseUnit _owner;
        private Dictionary<SkillHitType, List<CombatBuffTemplate>> _cbuffsByHitType;

        public CombatBuffs(BaseUnit owner)
        {
            _owner = owner;
            _cbuffsByHitType = new Dictionary<SkillHitType, List<CombatBuffTemplate>>();
        }
        
        public void AddCombatBuffs(uint buffId)
        {
            var buffsToAdd = SkillManager.Instance.GetCombatBuffs(buffId);

            foreach (var buffToAdd in buffsToAdd)
            {
                if (!_cbuffsByHitType.ContainsKey(buffToAdd.HitType))
                    _cbuffsByHitType.Add(buffToAdd.HitType, new List<CombatBuffTemplate>());
                _cbuffsByHitType[buffToAdd.HitType].Add(buffToAdd);
            }
        }

        public void RemoveCombatBuff(uint buffId)
        {
            var buffsToRemove = SkillManager.Instance.GetCombatBuffs(buffId);

            foreach (var buffToRemove in buffsToRemove)
            {
                if (!_cbuffsByHitType.ContainsKey(buffToRemove.HitType))
                    _cbuffsByHitType[buffToRemove.HitType].Remove(buffToRemove);
            }
        }

        public void TriggerCombatBuffs(Unit attacker, SkillHitType type)
        {
            if (!_cbuffsByHitType.ContainsKey(type))
                return;
            var buffs = _cbuffsByHitType[type];

            if (!(_owner is Unit unit))
                return;
            foreach (var cb in buffs)
            {
                // var caster = unit;
                // var target = attacker;
                // if (cb.BuffFromSource)
                //     caster = attacker;
                // if (cb.BuffToSource)
                //     target = unit;
                
                // TODO: Gotta figure out how to tell if it should be applied on getting hit, or on hitting
                
                var buffTempl = SkillManager.Instance.GetBuffTemplate(cb.BuffId);
                _owner.Effects.AddEffect(new Effect(_owner, attacker, new SkillCasterUnit(), buffTempl, null, DateTime.Now));
            }
        }
    }
}
