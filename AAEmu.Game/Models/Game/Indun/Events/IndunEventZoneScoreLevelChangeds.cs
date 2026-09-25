using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_zone_score_level_changeds</c> (3 rows, all zone group 130 on <c>zone_score_kind_id</c> 6,
/// the "인던 테스트" score of <c>zone_score_contents</c> 3): the copy's zone score reached <c>level</c>,
/// with <c>change_way</c> 0 any direction, 1 rising, 2 falling (row names: "1레벨로 변경 시 (레벨 하락
/// 시에만)", "3레벨로 변경 시 (레벨 상승 시에만)"). The catalogs are loaded by
/// <c>FactionScoringGameData</c>, but the server does not own or apply zone-score state and
/// <c>SCZoneScoreUpdatePacket</c> has no sender, so the event remains inert.
/// </summary>
internal class IndunEventZoneScoreLevelChangeds : IndunEvent
{
    public uint SourceFactionId { get; set; }
    public uint ZoneScoreKindId { get; set; }
    public int Level { get; set; }
    public int ChangeWay { get; set; }

    public override void Subscribe(WorldInstance worldInstance)
    {
        Logger.Debug($"IndunEventZoneScoreLevelChanged {Id}: zone score kind {ZoneScoreKindId} level {Level} way {ChangeWay} is not tracked, world {worldInstance?.Id}");
    }
}
