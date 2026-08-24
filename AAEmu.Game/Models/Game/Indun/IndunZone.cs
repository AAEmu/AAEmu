namespace AAEmu.Game.Models.Game.Indun;

public class IndunZone
{
    /// <summary>
    /// ZoneGroupId for this dungeon
    /// </summary>
    public uint ZoneGroupId { get; init; }
    // 10.0.2.13: name, comment, item_id removed from indun_zones
    /// <summary>
    /// Minimum character level required to enter this dungeon
    /// </summary>
    public uint LevelMin { get; init; } = 1;
    /// <summary>
    /// Maximum level a character can have to enter this dungeon
    /// </summary>
    public uint LevelMax { get; init; } = 100;

    /// <summary>
    /// Minimum gear score required to enter (indun_zones.gear_score; 0 = no requirement).
    /// </summary>
    public uint GearScore { get; init; }
    /// <summary>
    /// Maximum number of players in this dungeon
    /// </summary>
    public uint MaxPlayers { get; init; } = 9999;
    /// <summary>
    /// Set to false, then PvP is NOT allowed in this dungeon (used for Mirage and Library)
    /// </summary>
    public bool PvP { get; init; } = true;
    /// <summary>
    /// If this dungeon has its own respawn points, not sure how this is used
    /// </summary>
    public bool HasGraveyard { get; init; } = true;
    /// <summary>
    /// Minimum time in seconds between repeat dungeon creations by the same player
    /// </summary>
    public uint RestoreItemTime { get; init; }
    /// <summary>
    /// Does the player need to be part of a party in order to enter this dungeon
    /// </summary>
    public bool PartyOnly { get; init; }
    /// <summary>
    /// Is this dungeon only run clients-side
    /// </summary>
    public bool ClientDriven { get; init; }
    /// <summary>
    /// Does this dungeon have a channel select
    /// </summary>
    public bool SelectChannel { get; init; }

    /// <summary>
    /// Maximum number of times a player can entry this instance (0 = can't enter).
    /// Loaded from <c>instances.enter_count</c> where <c>target_type=IndunZone</c>.
    /// </summary>
    public uint EnterCount { get; set; } = 1000;

    /// <summary>
    /// Catalog id from <c>instances.id</c> (visit-count wire <c>data</c> field). 0 if none.
    /// </summary>
    public uint InstanceCatalogId { get; set; }

    /// <summary><c>instances.reset_item_id</c> for IVT_RESET tickets.</summary>
    public uint ResetItemId { get; set; }
    /// <summary><c>instances.reset_limit</c> (0 = unlimited).</summary>
    public int ResetLimit { get; set; }
    /// <summary><c>instances.reset_item_increase_scale</c>.</summary>
    public int ResetItemIncreaseScale { get; set; } = 1;
    /// <summary><c>instances.permit_enter_count_item_id</c> for IVT_PERMIT tickets.</summary>
    public uint PermitEnterCountItemId { get; set; }

    /// <summary><c>instances.direct_matching</c> — enter Indun after match (not InstantGame PvP).</summary>
    public bool DirectMatching { get; set; }

    /// <summary><c>instances.matching_invitation_type_id</c> (0 DIRECT, 1 PERFECT).</summary>
    public byte MatchingInvitationTypeId { get; set; }

    /// <summary><c>instances.min_matching_time</c> in milliseconds.</summary>
    public uint MinMatchingTimeMs { get; set; }

    /// <summary><c>instances.apply_waiting_time</c> in milliseconds (max queue lifetime).</summary>
    public uint ApplyWaitingTimeMs { get; set; }

    /// <summary><c>instances.matching_cleanup_term</c> in milliseconds (invite window).</summary>
    public uint MatchingCleanupTermMs { get; set; }

    /// <summary><c>instances.matching_intergration_level_id</c> (stored; multi-World unused locally).</summary>
    public byte MatchingIntegrationLevelId { get; set; }

    /// <summary>
    /// Cached localized name of this dungeon
    /// </summary>
    public string LocalizedName { get; set; }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(LocalizedName) ? string.Empty : LocalizedName;
    }
}
