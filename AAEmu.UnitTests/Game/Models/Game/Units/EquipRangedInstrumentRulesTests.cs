using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

/// <summary>
/// Songcraft equip_ranged=1: combat instruments (useAsStat) pass whether or not they have an
/// <c>instrument_sounds</c> row (Wayfaring Master has none; Illustrious Lake Flute has one).
/// Play-only instruments (useAsStat false — tooltip caution) fail.
/// </summary>
public class EquipRangedInstrumentRulesTests
{
    [Test]
    public async Task CombatInstrument_UseAsStat_Passes()
    {
        var outcome = EquipRangedInstrumentRules.Evaluate(
            isCombatInstrumentShape: true, useAsStat: true, musicalSlotDisabled: false);

        await Assert.That(outcome).IsEqualTo(EquipRangedInstrumentRules.Outcome.Ok);
    }

    [Test]
    public async Task PlayOnlyInstrument_UseAsStatFalse_Fails()
    {
        var outcome = EquipRangedInstrumentRules.Evaluate(
            isCombatInstrumentShape: true, useAsStat: false, musicalSlotDisabled: false);

        await Assert.That(outcome)
            .IsEqualTo(EquipRangedInstrumentRules.Outcome.PlayOnlyInstrument);
        await Assert.That((uint)outcome).IsEqualTo(2u);
    }

    [Test]
    public async Task EmptyMusicalSlot_FailsAsMissing()
    {
        var outcome = EquipRangedInstrumentRules.Evaluate(
            isCombatInstrumentShape: false, useAsStat: false, musicalSlotDisabled: false);

        await Assert.That(outcome)
            .IsEqualTo(EquipRangedInstrumentRules.Outcome.MissingOrDisarmed);
        await Assert.That((uint)outcome).IsEqualTo(1u);
    }

    [Test]
    public async Task MusicalDisarm_FailsAsMissingEvenWithUseAsStat()
    {
        var outcome = EquipRangedInstrumentRules.Evaluate(
            isCombatInstrumentShape: true, useAsStat: true, musicalSlotDisabled: true);

        await Assert.That(outcome)
            .IsEqualTo(EquipRangedInstrumentRules.Outcome.MissingOrDisarmed);
    }
}
