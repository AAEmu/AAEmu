using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Items.Procs
{
    /// <summary>
    /// Instance of ItemProcTemplate. Keeps track of cooldown, "owner" item
    /// </summary>
    public class ItemProc
    {
        public uint TemplateId { get; set; }
        public ItemProcTemplate Template { get; set; }
        public DateTime LastProc { get; set; }

        public ItemProc(uint templateId)
        {
            TemplateId = templateId;
            Template = ItemManager.Instance.GetItemProcTemplate(templateId);
            LastProc = DateTime.MinValue;
        }

        public bool Apply(Unit owner, bool ignoreRoll = false)
        {
            if (DateTime.Now < LastProc.AddSeconds(Template.CooldownSec))
                return false;
            
            if (ignoreRoll || Rand.Next(0, 100) > Template.ChanceRate)
                return false;
            
            var caster = SkillCaster.GetByType(SkillCasterType.Unit);
            caster.ObjId = owner.ObjId;

            var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
            target.ObjId = owner.ObjId;
                
            var skill = new Skill(Template.SkillTemplate);
            skill.Use(owner, caster, target);
            return true;
        }
    }

}
