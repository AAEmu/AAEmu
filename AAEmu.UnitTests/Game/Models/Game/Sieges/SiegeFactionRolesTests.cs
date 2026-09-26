using AAEmu.Game.Models.Game.Sieges;

namespace AAEmu.UnitTests.Game.Models.Game.Sieges;

public class SiegeFactionRolesTests
{
    // The shipped shape: two alliances that can hold ground and attack, and one raider that can only attack.
    private const uint Raider = 114;
    private const uint FirstAlliance = 148;
    private const uint SecondAlliance = 149;

    private static SiegeFactionRoles Shipped() => SiegeFactionRoles.FromRows(
    [
        new SiegeFactionRole(Raider, 15, false, true),
        new SiegeFactionRole(FirstAlliance, 50, true, true),
        new SiegeFactionRole(SecondAlliance, 50, true, true),
    ]);

    [Test]
    public async Task FromRows_SinglesOutTheAllianceThatCanOnlyAttackAsTheRaider()
    {
        var roles = Shipped();

        await Assert.That(roles.RaiderFactionId).IsEqualTo(Raider);
        await Assert.That(roles.IsRaider(Raider)).IsTrue();
        await Assert.That(roles.IsRaider(FirstAlliance)).IsFalse();
        await Assert.That(roles.CanDefend(Raider)).IsFalse();
        await Assert.That(roles.CanDefend(FirstAlliance)).IsTrue();
    }

    [Test]
    public async Task FromRows_JoinsTheTwoTroopRowsOfAnAllianceIntoOneRole()
    {
        var roles = SiegeFactionRoles.FromRows(
        [
            new SiegeFactionRole(FirstAlliance, 50, false, false),
            new SiegeFactionRole(FirstAlliance, 50, true, false),
            new SiegeFactionRole(FirstAlliance, 50, false, true),
            new SiegeFactionRole(Raider, 15, false, true),
            new SiegeFactionRole(SecondAlliance, 50, true, true),
        ]);

        await Assert.That(roles.All.Count()).IsEqualTo(3);
        await Assert.That(roles.CanDefend(FirstAlliance)).IsTrue();
        await Assert.That(roles.CanDefend(SecondAlliance)).IsTrue();
    }

    [Test]
    public async Task OffenseAgainst_NamesTheOneAllianceThatAttacksTheDefender()
    {
        var roles = Shipped();

        await Assert.That(roles.OffenseAgainst(FirstAlliance)).IsEqualTo(SecondAlliance);
        await Assert.That(roles.OffenseAgainst(SecondAlliance)).IsEqualTo(FirstAlliance);
    }

    [Test]
    public async Task OffenseAgainst_RefusesADefenderThatCannotDefend()
    {
        var roles = Shipped();

        var ex = Assert.Throws<InvalidOperationException>(() => roles.OffenseAgainst(Raider));
        await Assert.That(ex.Message).Contains("cannot defend");
    }

    [Test]
    public async Task FromRows_RefusesContentThatNamesNoRaider()
    {
        // Two alliances that can attack but never defend would leave the outlaw side ambiguous.
        var rows = new[]
        {
            new SiegeFactionRole(Raider, 15, false, true),
            new SiegeFactionRole(SecondAlliance, 15, false, true),
            new SiegeFactionRole(FirstAlliance, 50, true, false),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => SiegeFactionRoles.FromRows(rows));
        await Assert.That(ex.Message).Contains("expected exactly one raider");
    }

    [Test]
    public async Task FromRows_RefusesContentThatNamesTwoRaiders()
    {
        var rows = new[]
        {
            new SiegeFactionRole(Raider, 15, false, true),
            new SiegeFactionRole(SecondAlliance, 15, false, true),
            new SiegeFactionRole(FirstAlliance, 50, true, true),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => SiegeFactionRoles.FromRows(rows));
        await Assert.That(ex.Message).Contains("expected exactly one raider");
    }

    [Test]
    public async Task OffenseAgainst_RefusesWhenOnlyTheRaiderIsLeftToAttack()
    {
        // A siege_factions row with no troop row can neither hold ground nor attack, so it is never named the
        // attacker: with no other alliance able to attack, the lookup refuses rather than picking one.
        var roles = SiegeFactionRoles.FromRows(
        [
            new SiegeFactionRole(Raider, 15, false, true),
            new SiegeFactionRole(FirstAlliance, 50, true, true),
            new SiegeFactionRole(SecondAlliance, 50, false, false),
        ]);

        var ex = Assert.Throws<InvalidOperationException>(() => roles.OffenseAgainst(FirstAlliance));
        await Assert.That(ex.Message).Contains("attacking alliances");
    }

    [Test]
    public async Task FromRows_RefusesAnEmptyRoster()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => SiegeFactionRoles.FromRows([]));
        await Assert.That(ex.Message).Contains("no alliances");
    }

    [Test]
    public async Task FromRows_RefusesAnAllianceThatCannotFieldAnything()
    {
        var rows = new[]
        {
            new SiegeFactionRole(Raider, 0, false, true),
            new SiegeFactionRole(FirstAlliance, 50, true, true),
            new SiegeFactionRole(SecondAlliance, 50, true, true),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => SiegeFactionRoles.FromRows(rows));
        await Assert.That(ex.Message).Contains("member_count 0");
    }
}
