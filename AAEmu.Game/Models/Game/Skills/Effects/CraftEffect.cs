using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Crafts;

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
            /*
            
             4.get wi_group_id from wi_group_wis using wi_id
             "wi_group_id will either be 1,2,3"
             */
            var group = CraftManager.Instance.GetWorldInteractionsByWiId(WorldInteractionId);
            var actions = HousingManager.Instance.GetHousingBuildStep(HousingId);
            var numActions = actions.NumActions;
            var groupId = group.wiGroupId;
            if (groupId == 1)
            {
                Character character = (Character)caster;
                character.Craft.EndCraft();
            }
            else if (groupId == 2)
            {
                //todo
            }
            //todo

            else if (groupId == 3)
            {
               //TODO: Take Item
            }
            

            _log.Debug("CraftEffect");
        }
    }
}
