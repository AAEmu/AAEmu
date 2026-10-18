namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// <c>unit_reqs</c> kind <c>equip_ranged</c> with value1 = 1 (musical instrument).
/// Songcraft needs a musical-slot Instrument that contributes combat stats
/// (<c>item_weapons.useAsStat</c>). Play-only instruments (Marianople Violin and kin —
/// tooltip caution that Songcraft is disabled while equipped) ship useAsStat false.
/// </summary>
public static class EquipRangedInstrumentRules
{
    public enum Outcome : byte
    {
        Ok = 0,
        /// <summary>No musical-slot Instrument weapon, or musical disarm.</summary>
        MissingOrDisarmed = 1,
        /// <summary>Instrument-shaped but useAsStat false (play-only / cosmetic).</summary>
        PlayOnlyInstrument = 2,
    }

    /// <param name="isCombatInstrumentShape">Musical slot holds a weapon whose holdable slot type is Instrument.</param>
    /// <param name="useAsStat"><c>item_weapons.useAsStat</c> on that weapon (false = play-only).</param>
    /// <param name="musicalSlotDisabled">A buff with <c>disarmament_musical</c> is active.</param>
    public static Outcome Evaluate(bool isCombatInstrumentShape, bool useAsStat, bool musicalSlotDisabled)
    {
        if (!isCombatInstrumentShape || musicalSlotDisabled)
            return Outcome.MissingOrDisarmed;
        if (!useAsStat)
            return Outcome.PlayOnlyInstrument;
        return Outcome.Ok;
    }
}
