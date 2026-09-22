using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// siege_offense_hq_user (relation 7) keeps the units of registered attackers while the zone group is under
/// siege: the extended offense HQ's own clouts (doodad 10561, funcs 3048 and 3059, buff 16868) and the test
/// plot 1795 area search name it, and nothing else does.
/// </summary>
public class SiegeHqTargetRulesTests
{
    private const uint Attacker = 11;
    private const uint SecondAttacker = 12;
    private const uint Defender = 13;

    /// <summary>What siege_raid_team_members holds with is_offense set for the zone group.</summary>
    private static readonly IReadOnlySet<uint> Offense = new HashSet<uint> { Attacker, SecondAttacker };

    [Test]
    public async Task ARegisteredAttackerDuringTheSiege_Qualifies()
    {
        await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(SiegePeriod.Siege, Attacker, Offense)).IsTrue();
        await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(SiegePeriod.Siege, SecondAttacker, Offense)).IsTrue();
    }

    [Test]
    public async Task ADefenderDuringTheSiege_DoesNot()
    {
        // The HQ belongs to the attacking side; a defender standing in its clout is not its user.
        await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(SiegePeriod.Siege, Defender, Offense)).IsFalse();
    }

    [Test]
    public async Task OutsideTheSiegePeriod_NobodyQualifies()
    {
        SiegePeriod[] notSiege =
        [
            SiegePeriod.NoDominion,
            SiegePeriod.HeroVolunteer,
            SiegePeriod.ReadyToSiege,
            SiegePeriod.Peace
        ];

        foreach (var period in notSiege)
        {
            await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(period, Attacker, Offense)).IsFalse();
        }
    }

    [Test]
    public async Task AUnitNobodyOwns_DoesNot()
    {
        // A wild NPC or a world doodad resolves to owner 0, and 0 is never a registration.
        await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(SiegePeriod.Siege, 0, Offense)).IsFalse();
    }

    [Test]
    public async Task WithoutARoster_NobodyQualifies()
    {
        // The registration could not be read, or nobody registered: refuse rather than guess a side.
        await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(SiegePeriod.Siege, Attacker, null)).IsFalse();
        await Assert.That(SiegeHqTargetRules.IsOffenseHqUser(SiegePeriod.Siege, Attacker, new HashSet<uint>())).IsFalse();
    }
}
