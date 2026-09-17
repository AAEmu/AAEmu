namespace AAEmu.Game.Models.Game.Units;

/// <summary>
/// One row of <c>slave_interaction_skills</c>: a skill a slave offers when a player interacts with it, and
/// what the slave has to be wearing before it is offered.
/// <para>
/// <see cref="RequireAttachPointId"/> names an attach point on the slave itself, not a slot index. The rows
/// that carry one are the passenger seats of a mount or cart: the seat exists once the seat item it belongs
/// to (a chair, a cart bench) has been fitted at that attach point, and <see cref="RequireEquipKindId"/> is
/// the kind that item counts as.
/// </para>
/// </summary>
public readonly record struct SlaveInteractionSkill(uint SkillId, bool Enabled, uint RequireAttachPointId, uint RequireEquipKindId);

/// <summary>
/// Who offers what. The table says which skills a slave has; this decides which of them the client is told
/// about.
/// </summary>
public static class SlaveInteractionRules
{
    /// <summary>
    /// The skills a slave offers: its enabled rows, in table order and without repeats. A row that asks for
    /// an attach point is offered only while that attach point holds an item of the kind the row names.
    /// </summary>
    /// <param name="equipKindAtAttachPoint">
    /// What the slave has at an attach point, as the equip kind of the item fitted there, or null when there
    /// is nothing there or no such slot. Pass null when the caller cannot look the slave's equipment up:
    /// rows that ask for gear then stay out, because offering a seat the slave does not have is worse than
    /// leaving it out.
    /// </param>
    public static List<uint> OfferedSkills(IEnumerable<SlaveInteractionSkill> rows,
        Func<uint, uint?> equipKindAtAttachPoint = null)
    {
        var skills = new List<uint>();
        if (rows == null)
            return skills;

        foreach (var row in rows)
        {
            if (!row.Enabled)
                continue;

            if (row.RequireAttachPointId != 0)
            {
                if (equipKindAtAttachPoint == null)
                    continue;

                var kind = equipKindAtAttachPoint(row.RequireAttachPointId);
                if (kind == null)
                    continue; // nothing fitted at that attach point

                if (row.RequireEquipKindId != 0 && kind != row.RequireEquipKindId)
                    continue; // something else is fitted there
            }
            else if (row.RequireEquipKindId != 0)
            {
                continue; // asks for a kind without naming an attach point to find it at
            }

            if (!skills.Contains(row.SkillId))
                skills.Add(row.SkillId);
        }

        return skills;
    }
}
