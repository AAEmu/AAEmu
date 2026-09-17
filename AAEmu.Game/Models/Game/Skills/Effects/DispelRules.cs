namespace AAEmu.Game.Models.Game.Skills.Effects;

/// <summary>
/// The pure part of <see cref="DispelEffect"/>: how many applications come off, and which side of the
/// Good/Bad split a tag removal belongs to.
/// </summary>
public static class DispelRules
{
    /// <summary>
    /// <c>dispel_effects.stack</c>: the number of applications one hit takes off a tagged buff.
    /// </summary>
    /// <remarks>
    /// 1,012 of the 3,168 rows author a non-zero stack (10 on 272, 1 on 540, 100 on 5, 250 on 1) and the
    /// column was not loaded at all, so a tagged dispel always took the dispel/cure count instead. 0 is the
    /// majority and means "no stack authored", which falls back to the count the row already used.
    /// </remarks>
    public static int StackCount(int stack, int dispelCount, int cureCount)
    {
        if (stack > 0)
            return stack;

        var count = Math.Max(dispelCount, cureCount);
        return count > 0 ? count : 1;
    }

    /// <summary>
    /// The buff kind a tagged removal targets, from the relation the caster has with the victim.
    /// </summary>
    /// <remarks>
    /// The tag path used to remove <c>max(dispel, cure)</c> applications whatever the row's kind was, so a
    /// cure-only row stripped the victim's good buffs and a dispel-only row stripped its debuffs. The
    /// Good/Bad split is the one the untagged path already uses: a hostile cast dispels Good, a friendly one
    /// cures Bad.
    /// </remarks>
    public static bool TargetsGoodBuffs(bool casterCanAttack, int dispelCount, int cureCount)
    {
        if (dispelCount > 0 && cureCount > 0)
            return casterCanAttack;
        if (dispelCount > 0)
            return true;
        if (cureCount > 0)
            return false;
        return casterCanAttack;
    }
}
