using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitReqNationTests
{
    [Test]
    public async Task RaceFactionResolvesToAlliance()
    {
        await Assert.That(UnitReqNation.EffectiveNationId((uint)FactionsEnum.Nuian, (uint)FactionsEnum.NuiaAlliance))
            .IsEqualTo((uint)FactionsEnum.NuiaAlliance);
        await Assert.That(UnitReqNation.EffectiveNationId((uint)FactionsEnum.Firran, (uint)FactionsEnum.HaranyaAlliance))
            .IsEqualTo((uint)FactionsEnum.HaranyaAlliance);
    }

    [Test]
    public async Task AllianceWithNoMotherKeepsItsId()
    {
        await Assert.That(UnitReqNation.EffectiveNationId((uint)FactionsEnum.NuiaAlliance, 0))
            .IsEqualTo((uint)FactionsEnum.NuiaAlliance);
    }

    [Test]
    public async Task SystemFactionsAreNotPlayerNations()
    {
        // The client compares the faction id with 1000; every system_factions
        // row (ids up to 221) is below it, so alliance and race members alike are not nation members.
        await Assert.That(UnitReqNation.IsPlayerNationMember((uint)FactionsEnum.Nuian)).IsFalse();
        await Assert.That(UnitReqNation.IsPlayerNationMember((uint)FactionsEnum.NuiaAlliance)).IsFalse();
        await Assert.That(UnitReqNation.IsPlayerNationMember((uint)FactionsEnum.HaranyaAlliance)).IsFalse();
        await Assert.That(UnitReqNation.IsPlayerNationMember(0)).IsFalse();
    }

    [Test]
    public async Task ThresholdIsInclusive()
    {
        await Assert.That(UnitReqNation.IsPlayerNationMember(UnitReqNation.PlayerNationFactionIdStart - 1)).IsFalse();
        await Assert.That(UnitReqNation.IsPlayerNationMember(UnitReqNation.PlayerNationFactionIdStart)).IsTrue();
        await Assert.That(UnitReqNation.IsPlayerNationMember(UnitReqNation.PlayerNationFactionIdStart + 1)).IsTrue();
    }
}
