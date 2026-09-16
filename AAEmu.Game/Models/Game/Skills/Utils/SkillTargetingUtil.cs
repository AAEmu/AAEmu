using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Utils;

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
                // Party membership, not "everyone the area found". This used to return the unfiltered
                // list, so a party-only effect hit every unit in radius. A caster with no party is
                // their own party, which is also how the client's party frame behaves.
                var partyTeam = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                return partyTeam == null
                    ? units.Where(o => o.ObjId == caster.ObjId)
                    : units.Where(o => o.ObjId == caster.ObjId || partyTeam.IsObjMember(o.ObjId));
            case SkillTargetRelation.Raid:
                var team = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                var mate = caster.ParentWorld.MateManager.GetActiveMates(caster.Id).FirstOrDefault(); // TODO: How to handle multiple pets?
                units = team == null ? units.Where(o => o.ObjId == mate?.ObjId) : units.Where(o => team.IsObjMember(o.ObjId));
                return units.Append(caster);
            case SkillTargetRelation.Others:
                return units.Where(o => caster.GetRelationStateTo(o) == RelationState.Neutral);
            case SkillTargetRelation.FriendlyForDebuff:
                // Friendly by faction, but deliberately targetable — this is the relation used by effects
                // that land a debuff on an ally, so the !CanAttack clause of Friendly must not apply.
                return units.Where(o => caster.GetRelationStateTo(o) == RelationState.Friendly);
            case SkillTargetRelation.Family:
                return units.Where(o => IsSameFamily(caster, o));
            case SkillTargetRelation.ExpeditionMember:
                return units.Where(o => IsSameExpedition(caster, o));
            case SkillTargetRelation.IgnoreProtected:
                // "Ignore protected" widens the selection rather than narrowing it: it means protection
                // flags do not exclude a unit. Nothing to filter here.
                return units;
            case SkillTargetRelation.SiegeOffenseHqUser:
            // Siege HQ ownership is not modelled server-side yet. Used by a single plot event; stays
            // permissive rather than silently emptying that event's target list.
            default:
                return units;
        }
    }

    private static bool IsSameFamily(BaseUnit caster, BaseUnit target)
    {
        return caster is Character { Family: > 0 } casterChar &&
               target is Character targetChar &&
               casterChar.Family == targetChar.Family;
    }

    private static bool IsSameExpedition(BaseUnit caster, BaseUnit target)
    {
        return caster is Unit { Expedition: not null } casterUnit &&
               target is Unit targetUnit &&
               targetUnit.Expedition?.Id == casterUnit.Expedition.Id;
    }

    public static bool IsRelationValid(SkillTargetRelation relation, BaseUnit caster, BaseUnit target)
    {
        switch (relation)
        {
            case SkillTargetRelation.Any:
                return true;
            case SkillTargetRelation.Friendly:
                return caster?.GetRelationStateTo(target) == RelationState.Friendly && !caster.CanAttack(target);
            case SkillTargetRelation.Hostile:
                return caster.CanAttack(target);
            case SkillTargetRelation.Party:
                // Party relation resolves against the same membership the area filter uses.
                if (target == caster) return true;
                return TeamManager.Instance.GetTeamByObjId(caster.ObjId)?.IsObjMember(target.ObjId) ?? false;
            case SkillTargetRelation.Raid:
                if (target == caster) return true;
                var team = TeamManager.Instance.GetTeamByObjId(caster.ObjId);
                return team?.IsObjMember(target.ObjId) ?? false;
            case SkillTargetRelation.Others:
                // return caster.GetRelationStateTo(target) == RelationState.Neutral;
                return caster?.ObjId != target.ObjId;
            case SkillTargetRelation.FriendlyForDebuff:
                return caster?.GetRelationStateTo(target) == RelationState.Friendly;
            case SkillTargetRelation.Family:
                return IsSameFamily(caster, target);
            case SkillTargetRelation.ExpeditionMember:
                return IsSameExpedition(caster, target);
            case SkillTargetRelation.IgnoreProtected:
            case SkillTargetRelation.SiegeOffenseHqUser:
            default:
                return true;
        }
    }
}
