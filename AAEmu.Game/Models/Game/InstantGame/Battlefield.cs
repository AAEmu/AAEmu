using AAEmu.Game.GameData;
namespace AAEmu.Game.Models.Game.InstantGame;

public class Battlefield
{
    public uint Id { get; set; }
    public uint InstanceId { get; set; }
    /// <summary>instance_rank_details.id serialized as the expedition history/rating type.</summary>
    public uint InstanceRankDetailId { get; set; }
    public uint InstanceUiKindId { get; set; }
    public bool SquadNotUse { get; set; }

    /// <summary>
    /// content: instances.apply_waiting_time (ms) for this battle field — how long an applicant may
    /// sit in the matchmaking queue before it expires and they are released. 0 disables the expiry.
    /// </summary>
    public uint ApplyWaitingTimeMs { get; set; }

    /// <summary>
    /// content: instances.matching_cleanup_term (ms) for this battle field — how long a match that
    /// has not filled may hold its invited players before it is abandoned and everyone is released.
    /// 0 disables the expiry.
    /// </summary>
    public uint MatchingCleanupTermMs { get; set; }

    public bool IsExpeditionContent => InstanceId != 0 &&
                                       InstanceUiKindId == BattlefieldGameData.ExpeditionInstanceUiKindId &&
                                       !SquadNotUse;
    public bool CanRecordExpeditionHistory => IsExpeditionContent && InstanceRankDetailId != 0;
    public uint ZoneKey { get; set; }
    public BattlefieldSpawns Spawns { get; set; }
    public GameRuleSet RuleSet { get; set; }
}
