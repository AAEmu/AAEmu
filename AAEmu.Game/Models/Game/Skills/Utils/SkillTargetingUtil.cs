using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Utils
{
    public static class SkillTargetingUtil
    {
        public static IEnumerable<T> FilterWithRelation<T>(SkillTargetRelation relation, T caster, IEnumerable<T> units) where T : BaseUnit
        {
            switch (relation)
            {
                case SkillTargetRelation.Any:
                    return units;
                case SkillTargetRelation.Friendly:
                    return units.Where(o => caster.GetRelationStateTo(o) == RelationState.Friendly && !caster.CanAttack(o));
                case SkillTargetRelation.Hostile:
                    return units.Where(caster.CanAttack);
                case SkillTargetRelation.Party:
                    return units;
                case SkillTargetRelation.Raid:
                    var team = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                    units = team == null ? new List<T>() : units.Where(o => team.IsObjMember(o.ObjId));
                    return units.Append(caster);
                case SkillTargetRelation.Others:
                    return units.Where(o => caster.GetRelationStateTo(o) == RelationState.Neutral);
                default:
                    return units;
            }
        }

        public static bool IsRelationValid(SkillTargetRelation relation, BaseUnit caster, BaseUnit target)
        {
            switch (relation)
            {
                case SkillTargetRelation.Any:
                    return true;
                case SkillTargetRelation.Friendly:
                    return caster.GetRelationStateTo(target) == RelationState.Friendly && !caster.CanAttack(target);
                case SkillTargetRelation.Hostile:
                    return caster.CanAttack(target);
                case SkillTargetRelation.Party:
                    return true;
                case SkillTargetRelation.Raid:
                    if (target == caster) return true;
                    var team = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                    return team?.IsObjMember(target.ObjId) ?? false;
                case SkillTargetRelation.Others:
                    return caster.GetRelationStateTo(target) == RelationState.Neutral;
                default:
                    return true;
            }
        }
    }
}
