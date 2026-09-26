using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Comparison rules of the unit_reqs kinds, recovered from the 10.0.2.13 client evaluator.
/// Each method mirrors one client handler; the caller supplies the state and the row operands.
/// The client record reads value1 at +4, value2 at +8 and value3 at +0xC of the kind field.
/// </summary>
public static class UnitReqOperatorRules
{
    /// <summary>
    /// Kinds 44 CrimePoint, 45 HonorPoint and 50 LivingPoint, 46 CrimeRecord,
    /// 47 JuryPoint, 77/78/127 leadership:
    /// value1 picks the bound and value2 is the threshold. 0 is "at least", anything else "at most".
    /// </summary>
    public static bool PassesBound(uint mode, long actual, long threshold)
        => mode == 0 ? actual >= threshold : actual <= threshold;

    /// <summary>
    /// Kinds 26, 95, 96, 97, 138 and 139:
    /// value1 0 compares the absolute pool, anything else the integer percent cur*100/max.
    /// "LessThan" kinds pass at or below value2, "MoreThan" kinds at or above it.
    /// </summary>
    public static bool PassesPoolCompare(uint mode, long current, long max, long threshold, bool lessThan)
    {
        long actual;
        if (mode == 0)
        {
            actual = current;
        }
        else
        {
            if (max <= 0)
                return false;
            actual = current * 100 / max;
        }

        return lessThan ? actual <= threshold : actual >= threshold;
    }

    /// <summary>
    /// Kinds 65 HealthMargin, 66 ManaMargin, 67 LaborPowerMargin and
    /// 99 LaborPowerMarginLocal: the free room max - current must reach value1.
    /// </summary>
    public static bool PassesMargin(long max, long current, long required)
        => max - current >= required;

    /// <summary>
    /// Kind 107 FullRechargedLaborPower fails with URK_FULL_RECHARGED_LABOR_POWER once the pool it
    /// reads is at its cap, so the kind passes while there is still room. The nine owning skills are the
    /// labor potions.
    /// </summary>
    public static bool PassesNotFullyRecharged(long current, long max)
        => current < max;

    /// <summary>
    /// Kind 122 GearScore: value1 0 needs gear score at least value2, anything else at most.
    /// </summary>
    public static bool PassesGearScore(uint mode, long gearScore, long threshold)
        => PassesBound(mode, gearScore, threshold);

    /// <summary>
    /// Kind 122 failure value: writes value2 into the u32, negated when value1 selects the upper bound.
    /// </summary>
    public static uint GearScoreDetail(uint mode, uint threshold)
        => mode == 0 ? threshold : unchecked((uint)-(int)threshold);

    /// <summary>
    /// Kind 40 FactionMatch: the unit's own faction or its mother faction equals value1.
    /// Kind 55 FactionMatchOnly keeps the exact match.
    /// </summary>
    public static bool PassesFactionMatch(uint factionId, uint motherId, uint value1)
        => factionId == value1 || motherId == value1;

    /// <summary>
    /// Kinds 42 MotherFaction and 56 MotherFactionOnly compare the root of value1
    /// with the root of the unit's faction through the faction service walk; a faction with no
    /// mother is its own root. Kind 59 is the negation.
    /// </summary>
    public static bool SharesRootFaction(uint unitFactionId, uint unitMotherId, uint value1FactionId, uint value1MotherId)
        => UnitReqNation.EffectiveNationId(unitFactionId, unitMotherId)
           == UnitReqNation.EffectiveNationId(value1FactionId, value1MotherId);

    /// <summary>
    /// Kind 75 LessActAbilityPoint failure value: packs value1 into the top bits (value1 * 0x2000000)
    /// and adds value2.
    /// </summary>
    public static uint LessActAbilityDetail(uint value1, uint value2)
        => unchecked((value1 << 25) + value2);

    /// <summary>
    /// Kind 90 ExpeditionLevel: the level lies within value1..value2, either order, both inclusive.
    /// </summary>
    public static bool PassesExpeditionLevel(uint value1, uint value2, long level)
    {
        var low = Math.Min(value1, value2);
        var high = Math.Max(value1, value2);
        return level >= low && level <= high;
    }

    /// <summary>Kind 71 detail when the character has no expedition (writes u16 0x328).</summary>
    public const ushort ExpeditionMemberNoExpeditionDetail = 0x328;

    /// <summary>Kind 71 detail when the member role is below value1 (writes u16 0x506).</summary>
    public const ushort ExpeditionMemberRoleDetail = 0x506;

    /// <summary>
    /// Kind 71 ExpeditionMember: needs an expedition, then the member's role byte (owner 255) at
    /// least value1. Returns 0 on pass, otherwise the u16 detail the client writes.
    /// </summary>
    public static ushort ExpeditionMemberDetail(int? role, uint value1)
    {
        if (role == null)
            return ExpeditionMemberNoExpeditionDetail;
        return role.Value >= value1 ? (ushort)0 : ExpeditionMemberRoleDetail;
    }

    /// <summary>
    /// Kind 79 Hero: value1 0 passes any seated hero; otherwise the seated record's grade byte
    /// must equal value1. A character without a seat has no record, so a non-zero value1 fails.
    /// </summary>
    public static bool PassesHero(uint value1, bool isHero, int heroGrade)
        => value1 == 0 ? isHero : heroGrade != 0 && heroGrade == value1;

    /// <summary>
    /// Kinds 116 Dual and 117 ExpeditionBattle: value1 picks the unit whose state is read, and
    /// value2 0 needs the state set, 1 needs it clear, any other value2 passes.
    /// </summary>
    public static bool PassesStateGate(uint value2, bool state)
        => value2 switch
        {
            0 => state,
            1 => !state,
            _ => true
        };

    /// <summary>
    /// The owner identity the dominion kinds compare: the expedition when the unit has one,
    /// otherwise the root of its faction. Expedition factions have no parent in the client's faction map.
    /// </summary>
    public static uint DominionOwnerKey(uint expeditionId, uint factionId, uint motherId)
        => expeditionId != 0 ? expeditionId : UnitReqNation.EffectiveNationId(factionId, motherId);

    /// <summary>
    /// A dominion row is owned by one identity: the claiming expedition, or the alliance for the faction-claimed
    /// siege zones (dominions.faction_id, zero on guild claims).
    /// </summary>
    public static bool IsDominionOwnedBy(uint dominionExpeditionId, uint dominionFactionId, uint ownerKey)
    {
        if (ownerKey == 0)
            return false;
        return dominionExpeditionId != 0
            ? dominionExpeditionId == ownerKey
            : dominionFactionId == ownerKey;
    }

    /// <summary>
    /// Kind 120 DominionCount: value2 0 needs at least value1 dominions (fails
    /// URK_DOMINION_COUNT_LESS), anything else at most value1 (fails URK_DOMINION_COUNT_MORE).
    /// </summary>
    public static bool PassesDominionCount(uint mode, int count, uint value1)
        => mode == 0 ? count >= value1 : count <= value1;

    /// <summary>
    /// Kind 129 ConflictZoneState: value1 selects a test on the zone group's conflict state
    /// (0: war or peace, 1: not peace, 2: not war), value2 is the expected test result. War is 6 and
    /// peace 7 in the client's state numbering, the same as <see cref="ZoneConflictType"/>.
    /// </summary>
    public static bool PassesConflictZoneState(uint value1, uint value2, ZoneConflictType state)
    {
        int test;
        switch (value1)
        {
            case 0:
                test = state is ZoneConflictType.War or ZoneConflictType.Peace ? 1 : 0;
                break;
            case 1:
                test = state != ZoneConflictType.Peace ? 1 : 0;
                break;
            case 2:
                test = state != ZoneConflictType.War ? 1 : 0;
                break;
            default:
                return false;
        }

        return value2 == test;
    }

    /// <summary>
    /// Kind 64 Housing and kind 83 House: value2 1 needs the match, anything else
    /// needs its absence.
    /// </summary>
    public static bool PassesHousing(uint value2, bool matches)
        => (value2 == 1) == matches;

    /// <summary>
    /// Kind 137 TowerDefStep: value3 is a one-based prog index within the tower's prog count and
    /// the running event's current step (as the active-info map holds it) must be value3 - 1.
    /// </summary>
    public static bool PassesTowerDefStep(int? currentStep, uint value3, int progCount)
        => value3 >= 1 && value3 <= progCount && currentStep == (int)value3 - 1;

    /// <summary>
    /// Kind 132 EnableArchePass: no pass in progress fails URK_ENABLE_ARCHE_PASS; value1 0 is the
    /// sentinel and accepts any pass, otherwise the pass in progress must be value1
    /// (URK_ENABLE_ARCHE_PASS_WITH_TYPE).
    /// </summary>
    public static bool PassesEnableArchePass(uint value1, bool anyPassInProgress, bool value1PassInProgress)
        => anyPassInProgress && (value1 == 0 || value1PassInProgress);
}
