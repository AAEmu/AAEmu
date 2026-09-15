using AAEmu.Game.Models.Game.Skills.Buffs;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The kind table is the only place that decides whether a <c>buff_triggers</c> row fires, so it has to
/// cover every id the content database defines.
/// </summary>
/// <remarks>
/// The snapshot below is <c>enum_buff_trigger_events</c> and the enabled-row counts from
/// <c>game_decrypted.sqlite3</c> (10.0.2.13):
/// <code>
/// SELECT e.id, e.name, count(t.id) FROM enum_buff_trigger_events e
///   LEFT JOIN buff_triggers t ON t.event_id = e.id AND t.enable = 't'
///   GROUP BY e.id ORDER BY e.id;
/// </code>
/// A checked-in snapshot rather than a live read: the content database is not part of the repository
/// (AAEmu.Game/Data/compact.sqlite3 is a placeholder), so a test cannot open it. The counts are pinned
/// only so a kind cannot be quietly dropped from the snapshot along with its table entry.
/// </remarks>
public class BuffTriggerKindRulesTests
{
    private static readonly (uint Id, string Name, int EnabledRows)[] ContentKinds =
    [
        (1, "attack", 62),
        (2, "attacked", 48),
        (3, "damage_any", 34),
        (4, "damaged", 443),
        (5, "dispelled", 136),
        (6, "timeout", 4060),
        (7, "damaged_melee", 20),
        (8, "damaged_ranged", 21),
        (9, "damaged_spell", 30),
        (10, "damaged_siege", 154),
        (11, "landing", 190),
        (12, "started", 5121),
        (13, "remove_on_move", 15),
        (14, "channeling_cancel", 1),
        (15, "remove_on_damaged", 14),
        (16, "death", 287),
        (17, "unmount", 20),
        (18, "kill", 56),
        (19, "damaged_collision", 1),
        (20, "immotality", 15),
        (21, "time", 256),
        (22, "kill_any", 13),
        (23, "any", 242),
        (24, "remove_need_buff", 13),
        (25, "user_cancel", 1),
        (26, "use_skill", 1),
        (27, "remove_stealth", 1),
        (28, "skill_controller", 0),
        (29, "absorption", 7),
        (30, "remove_aura", 0),
        (31, "breaker", 5),
        (32, "damage_melee", 7),
        (33, "damage_spell", 3),
        (34, "damage_range", 4),
        (35, "damage_siege", 1),
        (36, "system", 1)
    ];

    /// <summary>
    /// Every id the content database has is either bound to something that is raised or listed as not
    /// applicable: there is no third, silent outcome.
    /// </summary>
    [Test]
    public async Task EveryContentKindIsEitherWiredOrExplicitlyNotApplicable()
    {
        var classified = BuffTriggerKindRules.All.ToDictionary(binding => binding.DbId);

        await Assert.That(classified.Count).IsEqualTo(ContentKinds.Length);

        foreach (var (id, name, enabledRows) in ContentKinds)
        {
            await Assert.That(classified.ContainsKey(id)).IsTrue()
                .Because($"event_id {id} ({name}, {enabledRows} enabled rows) must be in the kind table");
        }
    }

    [Test]
    public async Task TheKindTableNamesEachIdTheWayTheContentDatabaseDoes()
    {
        var byId = BuffTriggerKindRules.All.ToDictionary(binding => binding.DbId);

        foreach (var (id, name, _) in ContentKinds)
        {
            var binding = byId[id];
            await Assert.That(binding.DbName).IsEqualTo(name);
            await Assert.That((uint)binding.Kind).IsEqualTo(id);
        }
    }

    /// <summary>An enum member that is not in the table would fall through the handler.</summary>
    [Test]
    public async Task TheEnumHasNoMemberOutsideTheKindTable()
    {
        var enumValues = Enum.GetValues<BuffEventTriggerKind>();

        await Assert.That(enumValues.Length).IsEqualTo(ContentKinds.Length);

        foreach (var kind in enumValues)
            await Assert.That(BuffTriggerKindRules.For(kind)).IsNotNull();
    }

    [Test]
    public async Task EveryBindingSaysHowItIsRaised()
    {
        foreach (var binding in BuffTriggerKindRules.All)
        {
            await Assert.That(binding.DbId).IsEqualTo((uint)binding.Kind);
            await Assert.That(string.IsNullOrWhiteSpace(binding.DbName)).IsFalse();
            await Assert.That(string.IsNullOrWhiteSpace(binding.Reason)).IsFalse()
                .Because($"kind {binding.DbId} ({binding.DbName}) needs a reason, wired or not");
        }
    }

    /// <summary>
    /// The counts are the evidence for the decisions above (which families dominate, which kinds have no
    /// rows at all), so a change to one has to be a deliberate change to the snapshot.
    /// </summary>
    [Test]
    public async Task TheSnapshotMatchesTheEnabledRowTotal()
    {
        var total = 0;
        foreach (var (_, _, enabledRows) in ContentKinds)
            total += enabledRows;

        await Assert.That(total).IsEqualTo(11283);
    }

    /// <summary>
    /// The kinds that could not be wired, with the ids they are. A kind may only leave this list by
    /// gaining a raise site, and the reason has to be updated with it.
    /// </summary>
    [Test]
    public async Task TheNotApplicableKindsAreTheKnownFive()
    {
        var notApplicable = BuffTriggerKindRules.All
            .Where(binding => binding.Wiring == BuffTriggerWiring.NotApplicable)
            .Select(binding => binding.Kind)
            .ToList();

        await Assert.That(notApplicable).IsEquivalentTo(new[]
        {
            BuffEventTriggerKind.Immotality,      // 20: no immortality state exists
            BuffEventTriggerKind.SkillController, // 28: 0 enabled rows, column unread
            BuffEventTriggerKind.RemoveAura,      // 30: 0 enabled rows, no aura state
            BuffEventTriggerKind.Breaker,         // 31: buff_breakers is not loaded (B5)
            BuffEventTriggerKind.System           // 36: 1 row, no event of its own
        });
    }

    [Test]
    public async Task ScheduledIsTheTimeKindAlone()
    {
        var scheduled = BuffTriggerKindRules.All
            .Where(binding => binding.Wiring == BuffTriggerWiring.Scheduled)
            .Select(binding => binding.Kind)
            .ToList();

        await Assert.That(scheduled).IsEquivalentTo(new[] { BuffEventTriggerKind.Time });
    }

    [Test]
    public async Task AnUnknownKindIsNotClaimedToBeWired()
    {
        await Assert.That(BuffTriggerKindRules.For((BuffEventTriggerKind)99)).IsNull();
        await Assert.That(BuffTriggerKindRules.WiringOf((BuffEventTriggerKind)99))
            .IsEqualTo(BuffTriggerWiring.NotApplicable);
    }

    #region time row offsets

    [Test]
    [Arguments(3000, 7000, 3000u)]     // buff 653: duration 7000, delay 3000 -> 3 s in
    [Arguments(0, 5000, 0u)]           // authored at the start
    [Arguments(-3000, 23000, 20000u)]  // buff 27016 (운수 좋은 날): 3 s before the end
    [Arguments(-10000, 300000, 290000u)] // buff 20392 (기갑병 탑승): 10 s before the end
    [Arguments(-9000, 5000, 0u)]       // before the buff starts: clamp to the start
    [Arguments(-3000, 0, 0u)]          // no end to count back from
    public async Task TimeOffsetIsReadFromTheBuffStartOrItsEnd(int delayTimeMs, int durationMs, uint expectedOffsetMs)
    {
        await Assert.That(BuffTriggerKindRules.ResolveTimeOffsetMs(delayTimeMs, durationMs))
            .IsEqualTo(expectedOffsetMs);
    }

    #endregion
}
