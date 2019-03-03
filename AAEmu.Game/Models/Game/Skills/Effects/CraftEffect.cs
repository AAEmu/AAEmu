using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class CraftEffect : EffectTemplate
    {
        public uint WorldInteractionId { get; set; }
        public uint HousingId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time)
        {
            //get group from wi_group_wis
            var group = CraftManager.Instance.GetWorldInteractionsByWiId(WorldInteractionId);
            var groupId = group.wiGroupId;
            //get Number of actions from housing_build_step
            /*
            var actions = HousingManager.Instance.GetHousingBuildStep(HousingId);
            var numActions = actions.NumActions;
            */
            if (groupId == 1)//World interaction group 1 : Crafting
            {
                Character character = (Character)caster;
                character.Craft.EndCraft();
            }
            else if (groupId == 2)//World interaction group 2 : Collecting
            {
                //todo
            }
            else if (groupId == 3)//World interaction group 3 : Building
            {
                //TODO: Take Item
            }


            _log.Debug("CraftEffect");
        }
    }
}
