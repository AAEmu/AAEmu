using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Who counts as a siege offense HQ user (enum_skill_target_relation 7, siege_offense_hq_user): a unit whose
/// owning character is registered on the offense side of the zone group's raid team while that zone group is
/// in its siege period.
/// </summary>
/// <remarks>
/// content, 10.0.2.13 game_decrypted: four rows name the relation and no skills row does. Two are the
/// extended offense HQ's own area (doodad 10561 공성 확장 진지, phase funcs DoodadFuncClout 3048 and 3059,
/// aoe_shapes 8905 and 8938 at 15 m, buff 16868 진지의 깃발), so the relation cannot mean "whoever stands
/// in the HQ area"; it names the side the HQ belongs to. One is plot event 14383 on test plot 1795 (skill
/// 29234), an area search for slaves (type flag 4). The HQ doodads 4307 and 10561 have faction 0 and
/// use_creator_faction 'f', and DoodadFuncDeclareSiege does nothing on this server, so ownership cannot be
/// read off a placed HQ. The one record of the attacking side the server keeps is
/// siege_raid_team_members.is_offense, and that is what decides. x2game-dev.dll FUN_396af850
/// (X2::GameClient::IsInSiegeHqClout) is the client's HQ-area test and reads the same clout radius; it says
/// nothing about ownership. Anything short of a registered attacker in a running siege is refused.
/// </remarks>
public static class SiegeHqTargetRules
{
    /// <summary>
    /// True when the unit owned by <paramref name="ownerCharacterId"/> (0 for a unit nobody owns) may be
    /// kept by a siege_offense_hq_user search. <paramref name="offenseRoster"/> is the zone group's offense
    /// registration, null when it could not be read.
    /// </summary>
    public static bool IsOffenseHqUser(SiegePeriod period, uint ownerCharacterId, IReadOnlySet<uint> offenseRoster)
        => period == SiegePeriod.Siege
           && ownerCharacterId != 0
           && offenseRoster != null
           && offenseRoster.Contains(ownerCharacterId);
}
