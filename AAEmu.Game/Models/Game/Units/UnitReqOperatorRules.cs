using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// Comparison rules of the unit_reqs kinds, recovered from the 10.0.2.13 client evaluator
/// (x2game-dev.dll: per-kind dispatcher 0x397964B0, default evaluator 0x392B1CD0). Each method
/// mirrors one native handler; the caller supplies the state and the row operands. The native
/// record reads value1 at +4, value2 at +8 and value3 at +0xC of the kind field.
/// </summary>
public static class UnitReqOperatorRules
{
    /// <summary>
    /// Kinds 44 CrimePoint (0x39793E70), 45 HonorPoint and 50 LivingPoint (0x397956B0), 46 CrimeRecord
    /// (0x39793EF0), 47 JuryPoint (0x39793F70), 77/78/127 leadership (0x39795780, 0x39794380, 0x39795810):
    /// value1 picks the bound and value2 is the threshold. 0 is "at least", anything else "at most".
    /// </summary>
    public static bool PassesBound(uint mode, long actual, long threshold)
        => mode == 0 ? actual >= threshold : actual <= threshold;

    /// <summary>
    /// Kinds 26 (0x392B17B0), 95 (0x392B1A50), 96 (0x392B1AF0), 97 (0x392B1B90), 138 (0x392B1850) and 139
    /// (0x392B1C30): value1 0 compares the absolute pool, anything else the integer percent cur*100/max.
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
    /// Kinds 65 HealthMargin (0x392B18F0), 66 ManaMargin (0x392B1970), 67 LaborPowerMargin (0x39794590) and
    /// 99 LaborPowerMarginLocal (0x39794620): the free room max - current must reach value1.
    /// </summary>
    public static bool PassesMargin(long max, long current, long required)
        => max - current >= required;

    /// <summary>
    /// Kind 107 FullRechargedLaborPower (0x39794A60) fails with URK_FULL_RECHARGED_LABOR_POWER once the pool it
    /// reads is at its cap, so the kind passes while there is still room. The nine owning skills are the
    /// labor potions.
    /// </summary>
    public static bool PassesNotFullyRecharged(long current, long max)
        => current < max;

    /// <summary>
    /// Kind 122 GearScore (0x392B0D50): value1 0 needs gear score at least value2, anything else at most.
    /// </summary>
    public static bool PassesGearScore(uint mode, long gearScore, long threshold)
        => PassesBound(mode, gearScore, threshold);

    /// <summary>
    /// Kind 122 failure value: 0x392B0D50 writes value2 into the u32, negated when value1 selects the upper bound.
    /// </summary>
    public static uint GearScoreDetail(uint mode, uint threshold)
        => mode == 0 ? threshold : unchecked((uint)-(int)threshold);

    /// <summary>
    /// Kind 40 FactionMatch (0x39793B30): the unit's own faction or its mother faction equals value1.
    /// Kind 55 FactionMatchOnly keeps the exact match.
    /// </summary>
    public static bool PassesFactionMatch(uint factionId, uint motherId, uint value1)
        => factionId == value1 || motherId == value1;

    /// <summary>
    /// Kinds 42 MotherFaction (0x397939D0) and 56 MotherFactionOnly (0x39793A30) compare the root of value1
    /// with the root of the unit's faction through the faction service walk 0x39CCD710; a faction with no
    /// mother is its own root. Kind 59 is the negation.
    /// </summary>
    public static bool SharesRootFaction(uint unitFactionId, uint unitMotherId, uint value1FactionId, uint value1MotherId)
        => UnitReqNation.EffectiveNationId(unitFactionId, unitMotherId)
           == UnitReqNation.EffectiveNationId(value1FactionId, value1MotherId);

    /// <summary>
    /// Kind 75 LessActAbilityPoint failure value: 0x39793DB0 packs value1 into the top bits (value1 * 0x2000000)
    /// and adds value2.
    /// </summary>
    public static uint LessActAbilityDetail(uint value1, uint value2)
        => unchecked((value1 << 25) + value2);

    /// <summary>
    /// Kind 90 ExpeditionLevel (0x39794830): the level lies within value1..value2, either order, both inclusive.
    /// </summary>
    public static bool PassesExpeditionLevel(uint value1, uint value2, long level)
    {
        var low = Math.Min(value1, value2);
        var high = Math.Max(value1, value2);
        return level >= low && level <= high;
    }

    /// <summary>Kind 71 detail when the character has no expedition (0x39794230 writes u16 0x328).</summary>
    public const ushort ExpeditionMemberNoExpeditionDetail = 0x328;

    /// <summary>Kind 71 detail when the member role is below value1 (0x39794230 writes u16 0x506).</summary>
    public const ushort ExpeditionMemberRoleDetail = 0x506;

    /// <summary>
    /// Kind 71 ExpeditionMember (0x39794230): needs an expedition, then the member's role byte (owner 255) at
    /// least value1. Returns 0 on pass, otherwise the u16 detail the client writes.
    /// </summary>
    public static ushort ExpeditionMemberDetail(int? role, uint value1)
    {
        if (role == null)
            return ExpeditionMemberNoExpeditionDetail;
        return role.Value >= value1 ? (ushort)0 : ExpeditionMemberRoleDetail;
    }

    /// <summary>
    /// Kind 79 Hero (0x39794430): value1 0 passes any seated hero; otherwise the seated record's grade byte
    /// must equal value1. A character without a seat has no record, so a non-zero value1 fails.
    /// </summary>
    public static bool PassesHero(uint value1, bool isHero, int heroGrade)
        => value1 == 0 ? isHero : heroGrade != 0 && heroGrade == value1;

    /// <summary>
    /// Kinds 116 Dual (0x39795D60, the duel id) and 117 ExpeditionBattle (0x39795E70): value2 0 needs the state
    /// set, 1 needs it clear, any other value2 passes.
    /// </summary>
    public static bool PassesStateGate(uint value2, bool state)
        => value2 switch
        {
            0 => state,
            1 => !state,
            _ => true
        };

    /// <summary>
    /// The owner identity the dominion kinds compare (0x396B6BE0): the expedition when the unit has one,
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
    /// Kind 120 DominionCount (0x39794DF0): value2 0 needs at least value1 dominions (fails
    /// URK_DOMINION_COUNT_LESS), anything else at most value1 (fails URK_DOMINION_COUNT_MORE).
    /// </summary>
    public static bool PassesDominionCount(uint mode, int count, uint value1)
        => mode == 0 ? count >= value1 : count <= value1;

    /// <summary>
    /// Kind 129 ConflictZoneState (0x39795130): value1 selects a test on the zone group's conflict state
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
    /// Kind 64 Housing (0x397961E0) and kind 83 House (0x392B0AE0): value2 1 needs the match, anything else
    /// needs its absence.
    /// </summary>
    public static bool PassesHousing(uint value2, bool matches)
        => (value2 == 1) == matches;

    /// <summary>
    /// Kind 137 TowerDefStep (0x39795310): value3 is a one-based prog index within the tower's prog count and
    /// the running event's current step (as the active-info map holds it) must be value3 - 1.
    /// </summary>
    public static bool PassesTowerDefStep(int? currentStep, uint value3, int progCount)
        => value3 >= 1 && value3 <= progCount && currentStep == (int)value3 - 1;

    /// <summary>
    /// Kind 132 EnableArchePass (0x39794EE0): no pass in progress fails URK_ENABLE_ARCHE_PASS; value1 0 (the
    /// sentinel at 0x3D4FCA74) accepts any pass, otherwise the pass in progress must be value1
    /// (URK_ENABLE_ARCHE_PASS_WITH_TYPE).
    /// </summary>
    public static bool PassesEnableArchePass(uint value1, bool anyPassInProgress, bool value1PassInProgress)
        => anyPassInProgress && (value1 == 0 || value1PassInProgress);
}
