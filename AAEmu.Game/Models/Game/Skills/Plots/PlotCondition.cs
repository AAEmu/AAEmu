using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;
using NLog;

namespace AAEmu.Game.Models.Game.Skills.Plots
{
    public class PlotCondition
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();

        public uint Id { get; set; }
        public bool NotCondition { get; set; }
        public PlotConditionType Kind { get; set; }
        public int Param1 { get; set; }
        public int Param2 { get; set; }
        public int Param3 { get; set; }

        public bool Check(Unit caster, SkillCaster casterCaster, BaseUnit target, SkillCastTarget targetCaster, SkillObject skillObject)
        {
            var res = true;
            switch (Kind)
            {
                case PlotConditionType.Level:
                    res = ConditionLevel(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3);
                    break;
                case PlotConditionType.Relation:
                    res = ConditionRelation(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3);
                    break;
                case PlotConditionType.Direction:
                    res = ConditionDirection(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3);
                    break;
                case PlotConditionType.BuffTag:
                    res = ConditionBuffTag(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.WeaponEquipStatus:
                    res = ConditionWeaponEquipStatus(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3); 
                    break;
                case PlotConditionType.Chance:
                    res = ConditionChance(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.Dead:
                    res = ConditionDead(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.CombatDiceResult:
                    res = ConditionCombatDiceResult(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3); // Every CombatDiceResult is a NotCondition -> false makes it true. 
                    break;
                case PlotConditionType.InstrumentType:
                    res = ConditionInstrumentType(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.Range:
                    res = ConditionRange(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.Variable:
                    res = ConditionVariable(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2, Param3);
                    break;
                case PlotConditionType.UnitAttrib:
                    res = ConditionUnitAttrib(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.Actability:
                    res = ConditionActability(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.Stealth:
                    res = ConditionStealth(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.Visible:
                    res = ConditionVisible(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
                case PlotConditionType.ABLevel:
                    res = ConditionABLevel(caster, casterCaster, target, targetCaster, skillObject, Param1, Param2,
                        Param3);
                    break;
            }

            _log.Debug("PlotCondition : {0} | Params : {1}, {2}, {3} | Result : {4}", Kind, Param1, Param2, Param3, NotCondition ? !res : res);            

            return NotCondition ? !res : res;
        }

        private static bool ConditionLevel(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int minLevel, int maxLevel, int unk3)
        {
            return caster.Level >= minLevel && caster.Level <= maxLevel;
        }

        private static bool ConditionRelation(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // Param1 is either 1, 4 or 5
            return true;
        }

        private static bool ConditionDirection(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // No params on this
            return true;
        }

        private static bool ConditionBuffTag(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int tagId, int unk2, int unk3)
        {
            return target.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId((uint)tagId));
        }

        private static bool ConditionWeaponEquipStatus(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int weaponEquipStatus, int unk2, int unk3)
        {
            // Weapon equip status can be :
            // 1 = ?
            // 2 = ?
            // 3 = ?
            return true;
        }
        
        private static bool ConditionChance(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int chance, int unk2, int unk3)
        {
            // Param2 is only used once, and its value is "1"
            var roll = Rand.Next(0, 100);
            return roll <= chance;
        }
        
        private static bool ConditionDead(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            var unitTarget = (Unit)target;
            return unitTarget.Hp == 0;
        }
        
        private static bool ConditionCombatDiceResult(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            return false; // Every CombatDiceResult is a NotCondition -> false makes it true.
        }
        
        private static bool ConditionInstrumentType(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // Param1 is either 21, 22 or 23
            return true;
        }
        
        private static bool ConditionRange(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int minRange, int maxRange, int unk3)
        {
            // Param1 = Min range
            // Param2 = Max range
            var range = MathUtil.CalculateDistance(caster.Position, target.Position);
            return range >= minRange && range <= maxRange;
        }
        
        private static bool ConditionVariable(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // All 3 params used. No idea.
            return true;
        }
        
        private static bool ConditionUnitAttrib(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // All 3 params used. No idea.
            return true;
        }

        private static bool ConditionActability(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int actabilityId, int op, int level)
        {
            // Check actability level
            // Param1 = Actability ID
            // Param2 = Operator (2, 3, 5) for equal, less than and less than or equal
            // Param3 = Actability Level
            return true;
        }
        
        private static bool ConditionStealth(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // unsure if player or target
            // only used for Flamebolt for some reason.
            // Also always a "NotCondition" so will default to false (result will be True)
            return false;
        }
        
        private static bool ConditionVisible(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int unk1, int unk2, int unk3)
        {
            // used for LOS ?
            return true;
        }
        private static bool ConditionABLevel(Unit caster, SkillCaster casterCaster, BaseUnit target,
            SkillCastTarget targetCaster, SkillObject skillObject, int flag, int min, int max)
        {
            // Unsure what Param1 is. Seems like a 3 bit flag
            // For Arc Lightning, we have a value of 7, using 3 different conditions
            // Need to find what the flags mean
            // Could be target/caster level ? 
            // 1 = caster ?
            // 2 = target ?
            // 3 = caster+target
            // 4 = ???
            // 7 = caster+target+???
            
            var level = caster.Level;
            return level >= min && level <= max;
        }
    }
}
