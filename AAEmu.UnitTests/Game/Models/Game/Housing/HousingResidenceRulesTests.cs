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
        await Assert.That(HousingTaxPaymentRules.ResolveTaxType(true, taxItemFeature: false))
            .IsEqualTo(HousingTaxPaymentRules.TaxTypeContribution);
        await Assert.That(HousingTaxPaymentRules.ResolveTaxType(false, taxItemFeature: true))
            .IsEqualTo(HousingTaxPaymentRules.TaxTypeSeal);
        // Ordinary houses with TaxItem off bill gold copper, not contribution.
        await Assert.That(HousingTaxPaymentRules.ResolveTaxType(false, taxItemFeature: false))
            .IsEqualTo(HousingTaxPaymentRules.TaxTypeGold);
        // Client Lua: HOUSING_TAX_SEAL=1, HOUSING_TAX_CONTRIBUTION=2.
        await Assert.That(HousingTaxPaymentRules.TaxTypeSeal).IsEqualTo((byte)1);
        await Assert.That(HousingTaxPaymentRules.TaxTypeContribution).IsEqualTo((byte)2);
        await Assert.That(HousingTaxPaymentRules.TaxTypeGold).IsEqualTo((byte)0);
    }

    [Test]
    public async Task ResolveOwningExpedition_PrefersResidenceNamedGuild()
    {
        var named = new SystemFaction { Id = (FactionsEnum)100 };
        var current = new SystemFaction { Id = (FactionsEnum)200 };

        await Assert.That(HousingResidenceRules.ResolveOwningExpedition(named, current).Id)
            .IsEqualTo(named.Id);
        await Assert.That(HousingResidenceRules.ResolveOwningExpedition(null, current).Id)
            .IsEqualTo(current.Id);
        await Assert.That(HousingResidenceRules.ResolveOwningExpedition(null, null)).IsNull();
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
        await Assert.That(HousingTaxPaymentRules.DepositCopper(200000)).IsEqualTo(400000);
        await Assert.That(HousingTaxPaymentRules.DepositCopper(0)).IsEqualTo(0);
    }

    [Test]
    public async Task RefundKind_MatchesAppliedCurrency()
    {
        await Assert.That(HousingTaxPaymentRules.RefundKind(null))
            .IsEqualTo(HousingTaxRefundKind.None);
        await Assert.That(HousingTaxPaymentRules.RefundKind(HousingTaxCharge.Empty))
            .IsEqualTo(HousingTaxRefundKind.None);

        // Prestige is reserved until the house txn commits — uncommitted means no refund.
        await Assert.That(HousingTaxPaymentRules.RefundKind(new HousingTaxCharge { ContributionPoints = 60 }))
            .IsEqualTo(HousingTaxRefundKind.None);
        await Assert.That(HousingTaxPaymentRules.RefundKind(new HousingTaxCharge
        {
            ContributionPoints = 60,
            ContributionCommitted = true
        })).IsEqualTo(HousingTaxRefundKind.Contribution);
        await Assert.That(HousingTaxPaymentRules.RefundKind(new HousingTaxCharge { CopperAmount = 1000, UsedAaPoint = false }))
            .IsEqualTo(HousingTaxRefundKind.Gold);
        await Assert.That(HousingTaxPaymentRules.RefundKind(new HousingTaxCharge { CopperAmount = 1000, UsedAaPoint = true }))
            .IsEqualTo(HousingTaxRefundKind.AaPoint);
        // Planned seals are not applied until the house commits — no item restore on failure.
        await Assert.That(HousingTaxPaymentRules.RefundKind(new HousingTaxCharge { BoundCertificates = 3, TaxCertificates = 1 }))
            .IsEqualTo(HousingTaxRefundKind.None);
    }
}
