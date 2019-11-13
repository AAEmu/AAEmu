using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Models.Game.Char
{
    public class CharacterSkills
    {
        public enum SkillType : byte
        {
            Skill = 1,
            Buff = 2
        }

        private List<uint> _removed;

        public Dictionary<uint, Skill> Skills { get; set; }
        public Dictionary<uint, PassiveBuff> PassiveBuffs { get; set; }

        public Character Owner { get; set; }

        public CharacterSkills(Character owner)
        {
            Owner = owner;
            Skills = new Dictionary<uint, Skill>();
            PassiveBuffs = new Dictionary<uint, PassiveBuff>();
            _removed = new List<uint>();
        }

        public void AddSkill(uint skillId)
        {
            var template = SkillManager.Instance.GetSkillTemplate(skillId);
            if (template.AbilityId > 0 &&
                template.AbilityId != (byte) Owner.Ability1 &&
                template.AbilityId != (byte) Owner.Ability2 &&
                template.AbilityId != (byte) Owner.Ability3)
                return;
            var points = ExpirienceManager.Instance.GetSkillPointsForLevel(Owner.Level);
            points -= GetUsedSkillPoints();
            if (template.SkillPoints > points)
                return;

            if (Skills.ContainsKey(skillId))
            {
                Skills[skillId].Level++;
                Owner.SendPacket(new SCSkillUpgradedPacket(Skills[skillId]));
            }
            else
                AddSkill(template, 1, true);
        }

        public void AddSkill(SkillTemplate template, byte level, bool packet)
        {
            var skill = new Skill
            {
                Id = template.Id,
                Template = template,
                Level = level
            };
            Skills.Add(skill.Id, skill);

            if (packet)
                Owner.SendPacket(new SCSkillLearnedPacket(skill));
        }
        
        public void AddBuff(uint buffId)
        {
            var template = SkillManager.Instance.GetPassiveBuffTemplate(buffId);
            if(template.AbilityId > 0 && 
               template.AbilityId != (byte)Owner.Ability1 && 
               template.AbilityId != (byte)Owner.Ability2 && 
               template.AbilityId != (byte)Owner.Ability3)
                return;
            var points = ExpirienceManager.Instance.GetSkillPointsForLevel(Owner.Level);
            points -= GetUsedSkillPoints();
            if(template.ReqPoints > points)
                return;
            if(PassiveBuffs.ContainsKey(buffId))
                return;
            var buff = new PassiveBuff();
            buff.Id = buffId;
            buff.Template = template;
            PassiveBuffs.Add(buff.Id, buff);
            Owner.BroadcastPacket(new SCBuffLearnedPacket(Owner.ObjId, buff.Id), true);
            // TODO apply buff effect
        }

        public void Reset(AbilityType abilityId) // TODO with price...
        {
            foreach (var skill in new List<Skill>(Skills.Values))
            {
                if (skill.Template.AbilityId != (byte)abilityId)
                    continue;
                Skills.Remove(skill.Id);
                _removed.Add(skill.Id);
            }

            foreach (var buff in new List<PassiveBuff>(PassiveBuffs.Values))
            {
                if (buff.Template.AbilityId != (byte)abilityId)
                    continue;
                PassiveBuffs.Remove(buff.Id);
                _removed.Add(buff.Id);
            }
            
            Owner.BroadcastPacket(new SCSkillsResetPacket(Owner.ObjId, abilityId), true);
        }

        public int GetUsedSkillPoints()
        {
            var points = 0;
            foreach (var skill in Skills.Values)
                points += skill.Template.SkillPoints;
            foreach (var buff in PassiveBuffs.Values)
                points += buff.Template.ReqPoints;
            return points;
        }

        public void Load(GameDBContext ctx)
        {
            var items = ctx.Skills.Where(s => s.Owner == Owner.Id);
            foreach(var it in items)
            {
                var type = (SkillType)Enum.Parse(typeof(SkillType), it.Type, true);
                switch (type)
                {
                    case SkillType.Skill:
                        var skill = (Skill)it;
                        Skills.Add(skill.Id, skill);
                        break;
                    case SkillType.Buff:
                        var buff = (PassiveBuff)it;
                        PassiveBuffs.Add(buff.Id, buff);
                        break;
                }
            }

            foreach (var skill in Skills.Values)
                if (skill != null)
                    skill.Template = SkillManager.Instance.GetSkillTemplate(skill.Id);
        }

        public void Save(GameDBContext ctx)
        {
            if (_removed.Count > 0)
            {
                ctx.Skills.RemoveRange(
                    ctx.Skills.Where(s => s.Owner == Owner.Id && _removed.Contains((uint)s.Id)));

                _removed.Clear();
            }
            ctx.SaveChanges();

            foreach (var skill in Skills.Values)
            {
                ctx.Skills.RemoveRange(
                    ctx.Skills.Where(s =>
                        s.Id == skill.Id &&
                        s.Owner == Owner.Id));
            }
            ctx.SaveChanges();

            ctx.Skills.AddRange(
                Skills.Values.Select(s => new DB.Game.Skills()
                    {
                        Id = s.Id,
                        Level = s.Level,
                        Type = SkillType.Skill.ToString(),
                        Owner = (int)Owner.Id,
                    }));

            ctx.SaveChanges();
            foreach (var buff in PassiveBuffs.Values)
            {
                ctx.Skills.RemoveRange(
                    ctx.Skills.Where(s =>
                        s.Id == buff.Id &&
                        s.Owner == Owner.Id));
            }
            ctx.SaveChanges();

            ctx.Skills.AddRange(
                PassiveBuffs.Values.Select(s => new DB.Game.Skills()
                {
                    Id = s.Id,
                    Level = 1,
                    Type = SkillType.Buff.ToString(),
                    Owner = (int)Owner.Id,
                }));
            ctx.SaveChanges();
        }
    }
}
