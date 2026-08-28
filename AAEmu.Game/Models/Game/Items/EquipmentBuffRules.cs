namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// When a ship part's grade buff may stay after that part leaves a slot.
/// </summary>
/// <remarks>
/// A last-copy removal used to ask "what would this part grant if one were still fitted?"
/// because the piece-count lookup floors an empty hull to one. That answered with the same
/// buff that had just been withdrawn, so the old grade stayed on the hull. A later copy at
/// a different grade then added its own buff beside it (the family is independent, not a
/// refresh), which is how a basic engine and a mythic engine both sat on one ship.
/// </remarks>
public static class EquipmentBuffRules
{
    /// <summary>
    /// Whether the buff taken off with a part should remain because copies of that part are
    /// still fitted and still earn that same buff.
    /// </summary>
    public static bool KeepWithdrawnBuff(int remainingCopies, uint stillEarnedBuffId, uint withdrawnBuffId) =>
        remainingCopies > 0 && stillEarnedBuffId != 0 && stillEarnedBuffId == withdrawnBuffId;

    /// <summary>
    /// Whether an older grade of the same part should come off when a new grade is fitted.
    /// Independent stack families do not replace each other, so a leftover basic engine
    /// would otherwise sit beside the mythic one.
    /// </summary>
    public static bool StripOtherGrade(uint incomingBuffId, uint otherBuffId, int copiesStillEarningOther) =>
        otherBuffId != 0 && otherBuffId != incomingBuffId && copiesStillEarningOther <= 0;
}
