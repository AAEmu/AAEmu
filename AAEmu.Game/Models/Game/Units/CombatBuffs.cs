using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
using NLog;

namespace AAEmu.Game.Models.Game.Units
{
    public class CombatBuffs
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
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
                if (_cbuffsByHitType.ContainsKey(buffToRemove.HitType))
                    _cbuffsByHitType[buffToRemove.HitType].Remove(buffToRemove);
            }
        }

        public void TriggerCombatBuffs(Unit attacker, Unit receiver, SkillHitType type, bool isHeal)
        {
            if (!_cbuffsByHitType.ContainsKey(type))
                return;
            var buffs = _cbuffsByHitType[type];

            if (!(_owner is Unit unit))
                return;
            foreach (var cb in buffs)
            {
                // // var caster = unit;
                // // var target = attacker;
                // // if (cb.BuffFromSource)
                // //     caster = attacker;
                // // if (cb.BuffToSource)
                // //     target = unit;
                //
                // // TODO: Gotta figure out how to tell if it should be applied on getting hit, or on hitting
                // var source = (Unit)_owner;
                // var target = (Unit)_owner;
                // if (IsDefendedAttack)
                // {
                //    if (cb.BuffToSource)
                //        target = attacker;
                //    if (cb.BuffFromSource)
                //        source = attacker;
                // }
                // else
                // {
                //     if (cb.BuffToSource)
                //         target = (Unit)_owner;
                //     if (cb.BuffFromSource)
                //         source = (Unit)_owner;
                // }

                if (cb.IsHealSpell != isHeal)
                    continue;

                // If BTS and we're not attacking, doesn't apply
                if (cb.BuffToSource && _owner != attacker)
                    continue;
                // If not BTS and we're attacking, doesn't apply
                if (!cb.BuffToSource && _owner == attacker)
                    continue;
                
                var target = unit;
                var source = unit;

                _log.Warn("[{0}, Req:{1}] BTS: {2} BFS: {3} HT: {4}", cb.BuffId, cb.ReqBuffId, cb.BuffToSource, cb.BuffFromSource, cb.HitType);

                var buffTempl = SkillManager.Instance.GetBuffTemplate(cb.BuffId);
                //if (cb.BuffToSource)
                if (!_owner.Buffs.CheckBuffImmune(cb.BuffId))
                    _owner.Buffs.AddBuff(new Buff(target, source, new SkillCasterUnit(source.ObjId), buffTempl, null, DateTime.Now));
            }
        }
    }
}
