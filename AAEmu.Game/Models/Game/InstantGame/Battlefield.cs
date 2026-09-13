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
    public bool IsExpeditionContent => InstanceId != 0 &&
                                       InstanceUiKindId == BattlefieldGameData.ExpeditionInstanceUiKindId &&
                                       !SquadNotUse;
    public bool CanRecordExpeditionHistory => IsExpeditionContent && InstanceRankDetailId != 0;
    public uint ZoneKey { get; set; }
    public BattlefieldSpawns Spawns { get; set; }
    public GameRuleSet RuleSet { get; set; }
}
