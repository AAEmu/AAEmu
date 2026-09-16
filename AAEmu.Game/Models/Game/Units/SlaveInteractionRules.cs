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
    /// The skills a slave offers: its enabled rows, in table order and without repeats. Rows that ask for
    /// an equipped slot are offered only when that slot holds an item of the kind they name.
    /// </summary>
    /// <param name="equipKindInSlot">
    /// What the slave's slot holds, as the item's equip kind, or null when the slot is empty or the item is
    /// not slave equipment. Pass null when the caller cannot look slots up: rows that ask for gear then stay
    /// out, because offering a skill the slave cannot use is worse than leaving it out.
    /// </param>
    public static List<uint> OfferedSkills(IEnumerable<SlaveInteractionSkill> rows,
        Func<uint, uint?> equipKindInSlot = null)
    {
        var skills = new List<uint>();
        if (rows == null)
            return skills;

        foreach (var row in rows)
        {
            if (!row.Enabled)
                continue;

            if (row.RequireSlotId != 0)
            {
                if (equipKindInSlot == null)
                    continue;

                var kind = equipKindInSlot(row.RequireSlotId);
                if (kind == null)
                    continue; // nothing in the slot

                if (row.RequireEquipKindId != 0 && kind != row.RequireEquipKindId)
                    continue; // something else in the slot
            }
            else if (row.RequireEquipKindId != 0)
            {
                continue; // asks for a kind without naming a slot to find it in
            }

            if (!skills.Contains(row.SkillId))
                skills.Add(row.SkillId);
        }

        return skills;
    }
}
