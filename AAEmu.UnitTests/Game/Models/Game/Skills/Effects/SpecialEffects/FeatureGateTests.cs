using AAEmu.Game.Models.Game.Features;
using AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

namespace AAEmu.UnitTests.Game.Models.Game.Skills.Effects.SpecialEffects;

/// <summary>
/// Synthesis and awakening are gated at their entry points, before any validation, RNG, payment,
/// reagent or item mutation. The gate has to be a runtime check rather than something inferred from how
/// the feature was named in configuration.
/// </summary>
public class FeatureGateTests
{
    private static FeatureSet With(Feature feature, bool enabled)
    {
        var features = new FeatureSet();
        features.Set(feature, enabled);
        return features;
    }

    [Test]
    public async Task Awakening_IsRefusedWhenDisabled()
    {
        await Assert.That(ItemChangeMapping.IsFeatureEnabled(With(Feature.itemChangeMapping, false))).IsFalse();
    }

    [Test]
    public async Task Awakening_IsAllowedWhenEnabled()
    {
        await Assert.That(ItemChangeMapping.IsFeatureEnabled(With(Feature.itemChangeMapping, true))).IsTrue();
    }

    [Test]
    public async Task Synthesis_IsRefusedWhenDisabled()
    {
        await Assert.That(ItemEvolving.IsFeatureEnabled(With(Feature.itemEvolving, false))).IsFalse();
    }

    [Test]
    public async Task Synthesis_IsAllowedWhenEnabled()
    {
        await Assert.That(ItemEvolving.IsFeatureEnabled(With(Feature.itemEvolving, true))).IsTrue();
    }

    [Test]
    public async Task NoFeatureSet_FailsClosed()
    {
        // Before the feature set is built, absence must read as "disabled" rather than as permission.
        await Assert.That(ItemChangeMapping.IsFeatureEnabled(null)).IsFalse();
        await Assert.That(ItemEvolving.IsFeatureEnabled(null)).IsFalse();
    }

    [Test]
    public async Task DefaultFeatureSet_LeavesBothDisabled()
    {
        var features = new FeatureSet();

        await Assert.That(ItemChangeMapping.IsFeatureEnabled(features)).IsFalse();
        await Assert.That(ItemEvolving.IsFeatureEnabled(features)).IsFalse();
    }

    [Test]
    public async Task AwakeningSharesItsBitWithDwarfWarborn()
    {
        // The two names address the same bit, so configuration cannot enable one without the other.
        // This is why the gate is an explicit call in the effect: the setting's spelling does not decide
        // whether awakening runs, the bit does.
        var viaAlias = With(Feature.dwarfWarborn, true);
        var viaFeature = With(Feature.itemChangeMapping, true);

        await Assert.That(ItemChangeMapping.IsFeatureEnabled(viaAlias)).IsTrue();
        await Assert.That(viaFeature.Check(Feature.dwarfWarborn)).IsTrue();
        await Assert.That((int)Feature.dwarfWarborn).IsEqualTo((int)Feature.itemChangeMapping);
    }

    [Test]
    public async Task ClearingTheAlias_DisablesAwakening()
    {
        var features = With(Feature.itemChangeMapping, true);
        features.Set(Feature.dwarfWarborn, false);

        await Assert.That(ItemChangeMapping.IsFeatureEnabled(features)).IsFalse();
    }

    [Test]
    public async Task TheTwoGates_AreIndependent()
    {
        var features = new FeatureSet();
        features.Set(Feature.itemEvolving, true);
        features.Set(Feature.itemChangeMapping, false);

        await Assert.That(ItemEvolving.IsFeatureEnabled(features)).IsTrue();
        await Assert.That(ItemChangeMapping.IsFeatureEnabled(features)).IsFalse();
    }
}
