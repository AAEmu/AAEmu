using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.Game.Models.Game.Indun.Events;

/// <summary>
/// <c>indun_event_zone_score_level_changeds</c> (3 rows, all zone group 130 on <c>zone_score_kind_id</c> 6,
/// the "인던 테스트" score of <c>zone_score_contents</c> 3): the copy's zone score reached <c>level</c>,
/// with <c>change_way</c> 0 any direction, 1 rising, 2 falling (row names: "1레벨로 변경 시 (레벨 하락
/// 시에만)", "3레벨로 변경 시 (레벨 상승 시에만)").
/// </summary>
/// <remarks>
/// No world event carries a zone-score change: the copy owns its <c>ZoneScoreRuntime</c>, sees every level
/// move on it, and calls <c>IndunManager.DoZoneScoreLevelChangedEvents</c>, which asks each loaded row
/// through <see cref="Matches"/> whether the move was its. Zone group 130's three rows change the copy's
/// scenery doodad (action 339/340/341), and that phase change is what carries the chain on to the round.
/// </remarks>
internal class IndunEventZoneScoreLevelChangeds : IndunEvent
{
    public uint SourceFactionId { get; set; }
    public uint ZoneScoreKindId { get; set; }
    public int Level { get; set; }
    public int ChangeWay { get; set; }

    /// <summary>
    /// True when <paramref name="application"/> is this row's kind landing on this row's level in this
    /// row's authored direction.
    /// </summary>
    public bool Matches(ZoneScoreApplication application) =>
        application.KindId == ZoneScoreKindId &&
        IndunRoundRules.ZoneScoreLevelChangeMatches(
            ChangeWay, Level, application.PreviousLevel, application.Level);

    public override void Subscribe(WorldInstance worldInstance)
    {
        // Deliberately no subscription: a zone-score change is not a world event. The copy raises it from
        // its own score runtime (see the remarks), so a row only has to answer Matches.
    }
}
