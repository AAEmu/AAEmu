using AAEmu.Game.Core.Managers;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// The in-memory offense roster behind the siege_offense_hq_user relation. The relation is tested once per unit
/// per area-trigger pass and the extended offense HQ's clout ticks every 200 ms, so the roster is read once and
/// kept until a registration changes. Every write to siege_raid_team_members is followed by an invalidation,
/// which is what these pin: a missed one leaves a player on the wrong side of the HQ for the rest of the siege.
/// </summary>
public class SiegeOffenseRosterCacheTests
{
    private const ushort SiegeZone = 1;
    private const ushort OtherZone = 2;

    private const uint Attacker = 11;
    private const uint SecondAttacker = 12;

    [Test]
    public async Task TheRoster_IsReadOnceForEveryCall()
    {
        var reads = 0;
        var cache = new SiegeOffenseRosterCache(zone =>
        {
            reads++;
            return [Attacker];
        });

        for (var i = 0; i < 10; i++)
            await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsTrue();

        // Ten relation checks in one area-trigger pass, one database read - not ten.
        await Assert.That(reads).IsEqualTo(1);
    }

    [Test]
    public async Task ARegisteringAttacker_IsSeenAfterTheInvalidationThatFollowsTheRegistration()
    {
        var roster = new HashSet<uint>();
        var cache = new SiegeOffenseRosterCache(zone => [.. roster]);

        await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsFalse();

        // SiegeManager.RegisterForRaidTeam writes the row, then drops the cached roster.
        roster.Add(Attacker);
        cache.Invalidate(SiegeZone);

        await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsTrue();
    }

    [Test]
    public async Task AnUnregisteringAttacker_IsGoneAfterTheInvalidationThatFollowsTheUnregistration()
    {
        var roster = new HashSet<uint> { Attacker, SecondAttacker };
        var cache = new SiegeOffenseRosterCache(zone => [.. roster]);

        await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsTrue();

        // SiegeManager.UnregisterFromRaidTeam deletes the row, then drops the cached roster.
        roster.Remove(Attacker);
        cache.Invalidate(SiegeZone);

        var after = cache.Get(SiegeZone);
        await Assert.That(after.Contains(Attacker)).IsFalse();
        await Assert.That(after.Contains(SecondAttacker)).IsTrue();
    }

    [Test]
    public async Task InvalidatingOneZoneGroup_LeavesTheOthersCached()
    {
        var reads = new Dictionary<ushort, int>();
        var cache = new SiegeOffenseRosterCache(zone =>
        {
            reads[zone] = reads.GetValueOrDefault(zone) + 1;
            return [Attacker];
        });

        _ = cache.Get(SiegeZone);
        _ = cache.Get(OtherZone);
        cache.Invalidate(SiegeZone);
        _ = cache.Get(SiegeZone);
        _ = cache.Get(OtherZone);

        await Assert.That(reads[SiegeZone]).IsEqualTo(2);
        await Assert.That(reads[OtherZone]).IsEqualTo(1);
    }

    [Test]
    public async Task AZoneGroupsRoster_IsItsOwnRegistration()
    {
        var cache = new SiegeOffenseRosterCache(zone => zone == SiegeZone ? [Attacker] : [SecondAttacker]);

        await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsTrue();
        await Assert.That(cache.Get(SiegeZone).Contains(SecondAttacker)).IsFalse();
        await Assert.That(cache.Get(OtherZone).Contains(SecondAttacker)).IsTrue();
        await Assert.That(cache.Get(OtherZone).Contains(Attacker)).IsFalse();
    }

    [Test]
    public async Task ForgettingEveryRoster_ReadsThemAgain()
    {
        // A deletion is not one zone group's registration: CharacterManager.DeleteCharacterAssets drops all of
        // them, because the roster query drops a deleted character (c.deleted = 0) it cannot name.
        var roster = new HashSet<uint> { Attacker };
        var reads = 0;
        var cache = new SiegeOffenseRosterCache(zone =>
        {
            reads++;
            return [.. roster];
        });

        _ = cache.Get(SiegeZone);
        _ = cache.Get(OtherZone);
        await Assert.That(reads).IsEqualTo(2);

        roster.Remove(Attacker);
        cache.InvalidateAll();

        await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsFalse();
        await Assert.That(cache.Get(OtherZone).Contains(Attacker)).IsFalse();
        await Assert.That(reads).IsEqualTo(4);
    }

    [Test]
    public async Task MutatingARosterTheCacheHandedOut_DoesNotChangeTheCachedOne()
    {
        var cache = new SiegeOffenseRosterCache(zone => [Attacker]);

        var handedOut = (HashSet<uint>)cache.Get(SiegeZone);
        handedOut.Remove(Attacker);

        await Assert.That(cache.Get(SiegeZone).Contains(Attacker)).IsTrue();
    }
}
