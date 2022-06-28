using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Items.Actions;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSkillHit : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint SkillId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is Character character)
            {
                if (SkillId > 0)
                {
                    var skillCaster = SkillCaster.GetByType(SkillCasterType.Item);
                    skillCaster.ObjId = ObjectIdManager.Instance.GetNextId();

                    var target = SkillCastTarget.GetByType(SkillCastTargetType.Doodad);
                    target.ObjId = owner.ObjId;

                    var skill = new Skill(SkillManager.Instance.GetSkillTemplate(SkillId));

                    if (skillCaster is SkillItem sc)
                    {
                        foreach (var skillEffect in skill.Template.Effects)
                        {
                            var template = SkillManager.Instance.GetEffectTemplate(skillEffect.EffectId);
                            if (template is SpecialEffect specialEffect)
                            {
                                var itemId = (uint)specialEffect.Value1;
                                sc.ItemTemplateId = itemId;
                                var item = ItemManager.Instance.Create(itemId, 1, 0);
                                var res = character.Inventory.Bag.AcquireDefaultItem(ItemTaskType.Loot, item.TemplateId, item.Count, item.Grade);
                                skill.Use(caster, skillCaster, target);
                            }
                        }
                    }
                }
            }
            owner.ToNextPhase = SkillId == skillId;
        }
    }
}
