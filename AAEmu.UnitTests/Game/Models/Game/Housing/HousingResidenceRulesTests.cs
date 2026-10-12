using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Housing;

public class HousingResidenceRulesTests
{
    [Test]
    public async Task IsExpeditionResidenceFamily_UsesTheShippedFamilyPrefix()
    {
        await Assert.That(HousingResidenceRules.IsExpeditionResidenceFamily("hs_expedition_house_complete")).IsTrue();
        await Assert.That(HousingResidenceRules.IsExpeditionResidenceFamily("hs_nuia_mansion_01_complete")).IsFalse();
        await Assert.That(HousingResidenceRules.IsExpeditionResidenceFamily(null)).IsFalse();
        await Assert.That(HousingResidenceRules.IsExpeditionResidenceFamily("")).IsFalse();
    }

    [Test]
    public async Task AssignExpeditionResidenceFaction_UsesTheExpeditionObjectNotSystemLookup()
    {
        var race = new SystemFaction { Id = (FactionsEnum)11 };
        var guild = new SystemFaction { Id = (FactionsEnum)22 };

        // Race faction on the house must be replaced by the expedition itself.
        await Assert.That(HousingResidenceRules.AssignExpeditionResidenceFaction(race, guild).Id)
            .IsEqualTo(guild.Id);
        // Already correct — keep the current reference.
        await Assert.That(ReferenceEquals(
            HousingResidenceRules.AssignExpeditionResidenceFaction(guild, guild), guild)).IsTrue();
        // Missing expedition leaves the current faction alone.
        await Assert.That(ReferenceEquals(
            HousingResidenceRules.AssignExpeditionResidenceFaction(race, null), race)).IsTrue();
    }

    [Test]
    public async Task ExpeditionResidence_PaysContributionNotSeals()
    {
        await Assert.That(HousingTaxPaymentRules.UsesContributionTax(true)).IsTrue();
        await Assert.That(HousingTaxPaymentRules.UsesContributionTax(false)).IsFalse();
        await Assert.That(HousingTaxPaymentRules.ResolveTaxType(true, taxItemFeature: true))
            .IsEqualTo(HousingTaxPaymentRules.TaxTypeContribution);
        await Assert.That(HousingTaxPaymentRules.ResolveTaxType(false, taxItemFeature: true))
            .IsEqualTo(HousingTaxPaymentRules.TaxTypeSeal);
        // Client Lua: HOUSING_TAX_SEAL=1, HOUSING_TAX_CONTRIBUTION=2 (not 0).
        await Assert.That(HousingTaxPaymentRules.TaxTypeSeal).IsEqualTo((byte)1);
        await Assert.That(HousingTaxPaymentRules.TaxTypeContribution).IsEqualTo((byte)2);
    }

    [Test]
    public async Task CurrencyUnits_CeilDivideByContentRatio()
    {
        // taxations.tax 200000 with contribution_gold_ratio 100 → 2000 prestige for one week.
        await Assert.That(HousingTaxPaymentRules.CurrencyUnits(200000, 100)).IsEqualTo(2000);
        // Place charges three weeks (deposit 2 + current 1) → 6000.
        await Assert.That(HousingTaxPaymentRules.CurrencyUnits(600000, 100)).IsEqualTo(6000);
        // Seals use tax_gold_ratio 10000.
        await Assert.That(HousingTaxPaymentRules.CurrencyUnits(600000, 10000)).IsEqualTo(60);
        await Assert.That(HousingTaxPaymentRules.CurrencyUnits(0, 100)).IsEqualTo(0);
    }
}
