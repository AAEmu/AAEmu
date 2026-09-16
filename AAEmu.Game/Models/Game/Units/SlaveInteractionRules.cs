namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// One row of <c>slave_interaction_skills</c>: a skill a slave offers when a player interacts with it, and
/// whatever it asks for before it is offered.
/// </summary>
public readonly record struct SlaveInteractionSkill(uint SkillId, bool Enabled, uint RequireSlotId, uint RequireEquipKindId);

/// <summary>
/// Who offers what. The table says which skills a slave has; this decides which of them the client is told
/// about.
/// </summary>
public static class SlaveInteractionRules
{
    /// <summary>
    /// The skills a slave offers: its enabled rows that ask for nothing, in table order and without
    /// repeats.
    /// </summary>
    /// <remarks>
    /// Rows that ask for an equipped slot *and* a kind are left out until the item-to-kind link exists:
    /// six of the 1183 shipped rows carry that condition, and offering a skill the slave cannot use is
    /// worse than leaving it out.
    /// </remarks>
    public static List<uint> OfferedSkills(IEnumerable<SlaveInteractionSkill> rows)
    {
        var skills = new List<uint>();
        if (rows == null)
            return skills;

        foreach (var row in rows)
        {
            if (!row.Enabled || row.RequireSlotId != 0 || row.RequireEquipKindId != 0)
                continue;

            if (!skills.Contains(row.SkillId))
                skills.Add(row.SkillId);
        }

        return skills;
    }
}
