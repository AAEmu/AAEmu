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
    public async Task WesternCharacterIsNotEastZoneNationMember()
    {
        var west = UnitReqNation.EffectiveNationId((uint)FactionsEnum.Nuian, (uint)FactionsEnum.NuiaAlliance);
        await Assert.That(UnitReqNation.IsNationMemberOfZone(west, (uint)FactionsEnum.HaranyaAlliance))
            .IsFalse();
        await Assert.That(UnitReqNation.IsNationMemberOfZone(west, (uint)FactionsEnum.NuiaAlliance))
            .IsTrue();
    }

    [Test]
    public async Task EmptyZoneFactionNeverMatches()
    {
        await Assert.That(UnitReqNation.IsNationMemberOfZone((uint)FactionsEnum.NuiaAlliance, 0))
            .IsFalse();
    }
}
