using AAEmu.Game.Models.Game.Skills.Static;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Static;

/// <summary>
/// Pins SkillResultErrorKeyToId: every SkillResultKeys member must land on the byte whose client symbol
/// (x2game-dev.dll FUN_39D23B10) spells that key back, except the keys the client has no symbol for, which
/// are pinned one by one to what the client's own evaluators write.
/// </summary>
public class SkillResultKeyMappingTests
{
    /// <summary>Keys the generic rule does not cover; each has its own test below.</summary>
    private static readonly HashSet<SkillResultKeys> Special =
    [
        SkillResultKeys.ok,
        SkillResultKeys.backpack_occupied,
        SkillResultKeys.skill_urk_unknown,
        SkillResultKeys.skill_urk_dominion_owner,
        SkillResultKeys.skill_check_character_p_stat_min,
        SkillResultKeys.skill_check_character_p_stat_max,
        SkillResultKeys.skill_urk_not_hero_not_candidate,
        SkillResultKeys.skill_urk_leadership_period,
        SkillResultKeys.skill_urk_skill_cooldown,
        SkillResultKeys.skill_urk_empty_slot_inventory,
        SkillResultKeys.skill_urk_combat_resource,
    ];

    private static byte Wire(SkillResultKeys key) => (byte)SkillResultHelper.SkillResultErrorKeyToId(key);

    /// <summary>The ui_texts key the client shows for the byte this key is sent as.</summary>
    private static string Shown(SkillResultKeys key) => SkillResultClientTable.KeyFor(SkillResultClientTable.SymbolFor(Wire(key)));

    [Test]
    public async Task EveryOtherKey_ShowsItsOwnMessage()
    {
        foreach (var key in Enum.GetValues<SkillResultKeys>().Where(k => !Special.Contains(k)))
        {
            await Assert.That($"{key} -> {Shown(key)}").IsEqualTo($"{key} -> {key}");
        }
    }

    [Test]
    public async Task Ok_IsSuccess()
    {
        await Assert.That(SkillResultHelper.SkillResultErrorKeyToId(SkillResultKeys.ok)).IsEqualTo(SkillResult.Success);
    }

    [Test]
    public async Task BackpackOccupied_IsTheBackpackByte()
    {
        // The key is spelled without the skill_ prefix; ui_texts carries both spellings for the same text.
        await Assert.That(Wire(SkillResultKeys.backpack_occupied)).IsEqualTo((byte)0x33);
        await Assert.That(Shown(SkillResultKeys.backpack_occupied)).IsEqualTo("skill_backpack_occupied");
    }

    [Test]
    public async Task KeysWithNoClientResult_FallToTheSilentByte()
    {
        // dominion_owner is not an enum_unit_req_kinds kind and the p_stat pair are not 10.0.2.13 results
        // (0x3C/0x3D are ITEM_SECURED and INVALID_ACCOUNT_ATTRIBUTE now); nothing emits them, and if
        // something did the client would show nothing rather than another result's text.
        foreach (var key in new[]
                 {
                     SkillResultKeys.skill_urk_unknown,
                     SkillResultKeys.skill_urk_dominion_owner,
                     SkillResultKeys.skill_check_character_p_stat_min,
                     SkillResultKeys.skill_check_character_p_stat_max,
                 })
        {
            await Assert.That(SkillResultHelper.SkillResultErrorKeyToId(key)).IsEqualTo(SkillResult.UrkUnknown);
        }
    }

    [Test]
    public async Task NotHeroNotCandidate_IsPlainFailure()
    {
        // Kind 128 has no symbol; the client evaluator FUN_39795090 writes FAILURE (0x01), so the text is
        // skill_failure, not skill_urk_not_hero ("Heroes cannot use this").
        await Assert.That(SkillResultHelper.SkillResultErrorKeyToId(SkillResultKeys.skill_urk_not_hero_not_candidate))
            .IsEqualTo(SkillResult.Failure);
        await Assert.That(Shown(SkillResultKeys.skill_urk_not_hero_not_candidate)).IsEqualTo("skill_failure");
    }

    [Test]
    public async Task LeadershipPeriod_ReusesTheLeadershipTotalByte()
    {
        // Kind 127 has no symbol; the client evaluator FUN_39795810 writes 0x90 (URK_LEADERSHIP_TOTAL) with
        // detail 0x355 (WRONG_LEADERSHIP_POINT). The detail is UnitReqs' job; the byte is pinned here.
        await Assert.That(SkillResultHelper.SkillResultErrorKeyToId(SkillResultKeys.skill_urk_leadership_period))
            .IsEqualTo(SkillResult.UrkLeadershipTotal);
        await Assert.That(Wire(SkillResultKeys.skill_urk_leadership_period)).IsEqualTo((byte)0x90);
    }

    [Test]
    public async Task SkillCooldown_IsTheKind102EvaluatorByte()
    {
        await Assert.That(Wire(SkillResultKeys.skill_urk_skill_cooldown)).IsEqualTo((byte)0xAC);
    }

    [Test]
    public async Task EmptySlotInventory_IsTheKind106EvaluatorByte()
    {
        await Assert.That(Wire(SkillResultKeys.skill_urk_empty_slot_inventory)).IsEqualTo((byte)0xB0);
    }

    [Test]
    public async Task CombatResource_IsTheKind136EvaluatorByte()
    {
        await Assert.That(Wire(SkillResultKeys.skill_urk_combat_resource)).IsEqualTo((byte)0xC7);
    }

    [Test]
    public async Task UnknownKey_FailsClosedToTheSilentByte()
    {
        await Assert.That(SkillResultHelper.SkillResultErrorKeyToId((SkillResultKeys)9999)).IsEqualTo(SkillResult.UrkUnknown);
    }
}
