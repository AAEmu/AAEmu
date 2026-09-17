namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Heal strength of the pet potions that carry the heal_pet special effect (type 56): <c>value1</c> and
/// <c>value2</c> are the low and high end of one percentage band, applied to the pet's maximum health.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: 9 <c>special_effects</c> rows of type 56, 7 of them reachable, and
/// every one of them sets the two slots to the same number — 10/10 on 66461, 20/20 on 3461 / 39276 / 39614,
/// 50/50 on the remaining five — so the pair is one band whose ends happen to coincide, not a range. They
/// are the 10 %, 20 % and 50 % tiers of 소환수 부상 치료 물약 (the item skills 15220, 40302, 40447, 49699,
/// 49488 and 49726). Those potions are used by the owner (<c>skills.target_selection_id</c> 1, source) and
/// heal the summon, so the band is read against the pet's own MaxHp.
/// </remarks>
public static class PetHealRules
{
    /// <summary>The percentage band a row names, low end first because a negative slot is not a percentage.</summary>
    public static (int Min, int Max) PercentBand(int value1, int value2)
    {
        var a = Math.Max(0, value1);
        var b = Math.Max(0, value2);
        return (Math.Min(a, b), Math.Max(a, b));
    }

    /// <summary>
    /// Health restored for one roll. Integer arithmetic on a long so a large pet cannot overflow, and an
    /// empty or unset percentage heals nothing rather than the whole bar.
    /// </summary>
    public static int AmountFor(int percent, int maxHp) =>
        percent <= 0 || maxHp <= 0 ? 0 : (int)((long)maxHp * percent / 100);
}
