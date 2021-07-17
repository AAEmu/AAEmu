using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncUse : DoodadFuncTemplate
    {
        public uint SkillId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if ((owner.DbHouseId > 0) && (caster is Character player))
            {
                // If it's on a house, need to check permissions
                var house = HousingManager.Instance.GetHouseById(owner.DbHouseId);
                if (house == null)
                {
                    caster.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                    _log.Warn("Interaction failed because attached house does not exist for doodad {0}", owner.ObjId);
                    return;
                }
                if (!house.AllowedToInteract(player))
                {
                    caster.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                    return;
                }
            }

            // TODO: check skill references and consume items if items are required for skills
            // Make caster cast skill ? 
            if (SkillId > 0)
            {
                var skillTemplate = SkillManager.Instance.GetSkillTemplate(SkillId);
                if (skillTemplate == null)
                {
                    owner.ToPhaseAndUse = false;
                    return;
                }
                var useSkill = new Skill(skillTemplate);
                TaskManager.Instance.Schedule(
                    new UseSkillTask(useSkill, caster, new SkillCasterUnit(caster.ObjId), owner,
                        new SkillCastDoodadTarget { ObjId = owner.ObjId }, null), TimeSpan.FromMilliseconds(0));
            }
            // TODO далее, после возврата, будет вызов GoToPhase
            //if (nextPhase > 0)
            //    owner.GoToPhase(null, nextPhase, skillId);

            owner.ToPhaseAndUse = skillId > 0;
        }
    }
}
