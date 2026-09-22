using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

using NLog;

namespace AAEmu.Game.Models.Game.Skills.Utils;

public static class SkillTargetingUtil
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

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
                // A caster in no raid keeps every one of its active mates, not the first one MateManager
                // lists (x2game-dev.dll FUN_39800cc0 admits a pet to a party or raid cast through its
                // owner). A mate casting resolves through its owner too; caster.Id was the mate's own id.
                var mates = ActiveMateObjIds(caster);
                units = team == null ? units.Where(o => mates.Contains(o.ObjId)) : units.Where(o => team.IsObjMember(o.ObjId));
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
                {
                    // The attacking side of the siege the caster stands in; see SiegeHqTargetRules.
                    var siege = SiegeOffenseContext(caster);
                    return units.Where(o => SiegeHqTargetRules.IsOffenseHqUser(
                        siege.Period, o.GetOwnerCharacter()?.Id ?? 0, siege.Roster));
                }
            default:
                return units;
        }
    }

    /// <summary>The active mates of the caster's owner (a character owns itself), by object id.</summary>
    private static HashSet<uint> ActiveMateObjIds(BaseUnit caster)
    {
        var owner = caster as Character ?? caster.GetOwnerCharacter();
        var mateManager = owner?.ParentWorld?.MateManager;
        return mateManager == null
            ? []
            : mateManager.GetActiveMates(owner.Id).Select(mate => mate.ObjId).ToHashSet();
    }

    /// <summary>
    /// The siege the caster stands in: its zone group's scheduled period and, during the siege itself, who
    /// registered as an attacker. Anything that cannot be resolved comes back as no roster, which
    /// <see cref="SiegeHqTargetRules"/> refuses; the reason is logged at Debug.
    /// </summary>
    private static (SiegePeriod Period, IReadOnlySet<uint> Roster) SiegeOffenseContext(BaseUnit caster)
    {
        var zone = caster?.Transform == null ? null : ZoneManager.Instance.GetZoneByKey(caster.Transform.ZoneId);
        if (zone == null)
        {
            Logger.Debug("siege_offense_hq_user: caster {0} stands in no known zone, nobody qualifies", caster?.ObjId ?? 0);
            return (SiegePeriod.NoDominion, null);
        }

        var zoneGroupId = (ushort)zone.GroupId;
        var period = SiegeManager.Instance.GetScheduledPeriod(zoneGroupId, DateTime.UtcNow);
        if (period != SiegePeriod.Siege)
        {
            Logger.Debug("siege_offense_hq_user: zone group {0} is in {1}, not Siege; nobody qualifies", zoneGroupId, period);
            return (period, null);
        }

        return (period, SiegeManager.Instance.GetOffenseRaidTeam(zoneGroupId));
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
            case SkillTargetRelation.SiegeOffenseHqUser:
                {
                    // The extended offense HQ's own clout (doodad 10561) reaches this through AreaTrigger.
                    var siege = SiegeOffenseContext(caster);
                    return SiegeHqTargetRules.IsOffenseHqUser(
                        siege.Period, target?.GetOwnerCharacter()?.Id ?? 0, siege.Roster);
                }
            case SkillTargetRelation.IgnoreProtected:
            default:
                return true;
        }
    }
}
