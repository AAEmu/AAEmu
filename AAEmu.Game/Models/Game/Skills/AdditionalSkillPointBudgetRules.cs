namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Level budget plus permanent bonus points from <c>SpecialType.AdditionalSkillPoint</c>.
/// </summary>
public static class AdditionalSkillPointBudgetRules
{
    /// <summary>Total points the character may spend: level table + bonus.</summary>
    public static int TotalBudget(int levelSkillPoints, uint additionalSkillPoints)
    {
        if (levelSkillPoints < 0)
            levelSkillPoints = 0;
        var sum = (long)levelSkillPoints + additionalSkillPoints;
        return sum > int.MaxValue ? int.MaxValue : (int)sum;
    }

    /// <summary>Points left to spend after learned skills/passives.</summary>
    public static int Remaining(int levelSkillPoints, uint additionalSkillPoints, int used)
    {
        if (used < 0)
            used = 0;
        return TotalBudget(levelSkillPoints, additionalSkillPoints) - used;
    }

    /// <summary>
    /// Next stored bonus after applying <paramref name="amount"/> (content uses 1). Rejects non-positive.
    /// </summary>
    public static bool TryAdd(uint current, int amount, out uint next)
    {
        next = current;
        if (amount <= 0)
            return false;

        var sum = (long)current + amount;
        next = sum >= uint.MaxValue ? uint.MaxValue : (uint)sum;
        return true;
    }
}
