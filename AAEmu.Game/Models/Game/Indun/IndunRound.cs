namespace AAEmu.Game.Models.Game.Indun;

/// <summary>
/// One <c>indun_rounds</c> row (77 rows: zone group 125 rounds 1..50, 126 rounds 1..21, 130 rounds 1..6).
/// The native loader is <c>LoadIndunRoundDescs</c> (x2game-dev.dll 0x39cc1830, x2game-dev_dedicate.dll
/// 0x39c88680): <c>SELECT id, boss_round, round, spawner_id, timer, zone_group_id</c>.
/// </summary>
public class IndunRound
{
    public uint Id { get; init; }
    public uint ZoneGroupId { get; init; }
    public int Round { get; init; }

    /// <summary>
    /// <c>spawner_id</c>: 188247..209919 for 125 and 126, 0 for 130. None of them exists in the client's
    /// <c>npc_spawners</c> (max id 24894), so a round spawn from this column fails closed at spawn time.
    /// </summary>
    public uint SpawnerId { get; init; }

    /// <summary>
    /// <c>timer</c>: 120 on the boss rounds of zone group 125 and on its round 1, 0 everywhere else.
    /// Read as seconds, the unit its sibling <c>indun_zones.option</c> times (ready_time, play_time) use.
    /// </summary>
    public int TimerSeconds { get; init; }

    /// <summary><c>boss_round</c>: 't' on 14 rows; the value behind <c>nextRoundBoss</c> in SCIndunRoundPlayStatus.</summary>
    public bool BossRound { get; init; }
}
