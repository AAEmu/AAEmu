using System;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks.Skills;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncUse : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint SkillId { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            if (caster is Character)
                _log.Debug("DoodadFuncUse: skillId {0}, nextPhase {1},  SkillId {2}", skillId, nextPhase, SkillId);
            else
                _log.Trace("DoodadFuncUse: skillId {0}, nextPhase {1},  SkillId {2}", skillId, nextPhase, SkillId);

            if (caster == null)
            {
                return;
            }

            if ((owner.DbHouseId > 0) && (caster is Character player))
            {
                // If it's on a house, need to check permissions
                var house = HousingManager.Instance.GetHouseById(owner.DbHouseId);
                if (owner is DoodadCoffer coffer)
                {
                    // Coffers need their own permissions as they can override the house's settings
                    if (!coffer.AllowedToInteract(player))
                    {
                        caster.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                        return;
                    }
                }
                else if (house == null)
                {
                    //caster.SendErrorMessage(ErrorMessageType.InteractionPermissionDeny);
                    // Added fail-safe in case a doodad wasn't properly deleted from a house
                    // The first try to recover the doodad will still give a error, but after that, it's free to recover by anyone. 
                    owner.DbHouseId = 0;
                    owner.OwnerId = 0;
                    _log.Trace("Interaction failed because attached house does not exist for doodad {0}, resetting DbHouseId to public", owner.ObjId);
                    //return;
                }
                else if (!house.AllowedToInteract(player))
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
                    return;
                }
                var useSkill = new Skill(skillTemplate);
                TaskManager.Instance.Schedule(new UseSkillTask(useSkill, caster, new SkillCasterUnit(caster.ObjId), owner, new SkillCastDoodadTarget { ObjId = owner.ObjId }, null), TimeSpan.FromMilliseconds(0));
            }
        
            owner.ToNextPhase = skillId > 0;
        }
    }
}
