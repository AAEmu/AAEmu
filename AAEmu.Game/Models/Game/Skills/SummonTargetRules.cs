namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Which of the caster's own summons a pet, my_slave or child_slave cast lands on (enum_skill_target_type
/// 10, 26 and 21).
/// </summary>
/// <remarks>
/// Resolves these before anything else is validated. A pet cast (10) ignores the
/// unit the packet names and takes the first pet of the caster's own pet list (walks the pet
/// manager's collection and returns the first live entry), NO_TARGET when there is none. A my_slave cast
/// (26) takes the one unit the slave manager holds as "my slave", the hull the server
/// announced with SCMySlavePacket. A child_slave cast (21) has no branch of its own: it takes the unit the
/// packet names, so the server has to check that unit is a part of the caster's own slave. The owned lists
/// are passed in world order, so the first entry is the earliest summon.
///
/// content, 10.0.2.13 game_decrypted: 14 skills target a pet (the potions 21628, 22668 and 29798 through
/// items 27387, 28359 and 38212, the pet emotes 44214 and 44775..44778, 48423 소환수 은신 해제, the plot
/// skills 37028, 39924, 40112, 46605 and 49211), 2 target my_slave (42183 선박 강화, 42280 함포 효율성
/// 강화) and 1 targets child_slave (42943, a test). None is an Npc kit row.
/// </remarks>
public static class SummonTargetRules
{
    /// <summary>
    /// The pet or my_slave cast lands on the named summon when it is one of the caster's own, otherwise on
    /// the first one owned; 0 when the caster owns none.
    /// </summary>
    public static uint Pick(IReadOnlyList<uint> ownedObjIds, uint requestedObjId)
    {
        if (ownedObjIds == null || ownedObjIds.Count == 0)
            return 0;
        if (requestedObjId != 0 && ownedObjIds.Contains(requestedObjId))
            return requestedObjId;
        return ownedObjIds[0];
    }

    /// <summary>
    /// The child_slave cast lands on the named unit only when it is one of the caster's own parts; 0 when
    /// nothing was named or the name is someone else's.
    /// </summary>
    public static uint PickNamed(IReadOnlyList<uint> ownedObjIds, uint requestedObjId)
        => requestedObjId != 0 && ownedObjIds != null && ownedObjIds.Contains(requestedObjId)
            ? requestedObjId
            : 0;
}
