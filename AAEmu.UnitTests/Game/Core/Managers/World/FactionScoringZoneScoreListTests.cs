using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.World.Zones;

namespace AAEmu.UnitTests.Game.Core.Managers.World;

/// <summary>
/// The zone-score list sender over a real catalog, so the ordering and the wire-range clamp are
/// exercised against content rather than against a hand-built list.
/// </summary>
[NotInParallel]
public sealed class FactionScoringZoneScoreListTests : SqliteTestBase
{
    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        using var command = Connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE npcs (id INTEGER PRIMARY KEY);
            CREATE TABLE buffs (id INTEGER PRIMARY KEY);
            CREATE TABLE quest_contexts (id INTEGER PRIMARY KEY);
            CREATE TABLE enum_faction_competition_reset_state_kinds (id INTEGER PRIMARY KEY, name TEXT NOT NULL);
            CREATE TABLE faction_competitions (
                id INTEGER PRIMARY KEY, comments TEXT NOT NULL, point_pc_kill_value INTEGER NOT NULL,
                point_npc_kill_value INTEGER NOT NULL, point_quest_complete_value INTEGER NOT NULL,
                req_point INTEGER NOT NULL, point_reset_id INTEGER NOT NULL, force_change_state TEXT NOT NULL,
                force_stop_tower_def_id INTEGER NULL, tooltip TEXT NOT NULL, detail_id INTEGER NOT NULL,
                detail_type TEXT NOT NULL
            );
            CREATE TABLE faction_competition_npc_infos (
                id INTEGER PRIMARY KEY, faction_competition_id INTEGER NOT NULL, npc_id INTEGER NOT NULL
            );
            CREATE TABLE faction_competition_quest_infos (
                id INTEGER PRIMARY KEY, faction_competition_id INTEGER NOT NULL, context_id INTEGER NOT NULL
            );
            CREATE TABLE zone_score_contents (
                id INTEGER PRIMARY KEY, name TEXT NOT NULL, show_hud TEXT NOT NULL, zone_group_id INTEGER NOT NULL,
                buff_id INTEGER NOT NULL, quest_id INTEGER NOT NULL
            );
            CREATE TABLE zone_score_kinds (
                id INTEGER PRIMARY KEY, content_id INTEGER NOT NULL, ui_order INTEGER NOT NULL, db_save TEXT NOT NULL,
                score_name TEXT NOT NULL, icon_id INTEGER NOT NULL, max_score INTEGER NOT NULL, show_score TEXT NOT NULL,
                show_max_score TEXT NOT NULL, level_name TEXT NOT NULL, show_max_level TEXT NOT NULL,
                reset_zone_in TEXT NOT NULL, reset_zone_out TEXT NOT NULL, reset_buff_destroyed TEXT NOT NULL,
                reset_quest_removed TEXT NOT NULL
            );
            CREATE TABLE zone_score_levels (
                id INTEGER PRIMARY KEY, kind_id INTEGER NOT NULL, level INTEGER NOT NULL, req_score INTEGER NOT NULL,
                buff_id INTEGER NOT NULL
            );
            CREATE TABLE zone_score_kind_rank_details (id INTEGER PRIMARY KEY, zone_score_kind_id INTEGER NOT NULL);

            INSERT INTO npcs VALUES (100);
            INSERT INTO buffs VALUES (300);
            INSERT INTO quest_contexts VALUES (200);
            INSERT INTO enum_faction_competition_reset_state_kinds VALUES (1, 'all');
            INSERT INTO faction_competitions VALUES (10, 'sample', 1, 2, 3, 100, 1, 'f', NULL, '', 1, 'CompetitionPvp');
            INSERT INTO faction_competition_npc_infos VALUES (1, 10, 100);
            INSERT INTO faction_competition_quest_infos VALUES (1, 10, 200);
            INSERT INTO zone_score_contents VALUES (1, 'score', 't', 5, 300, 0);
            -- Kind 1 is ordered third and kind 2 is ordered first, so a sender that used kind id
            -- order instead of ui_order would produce a different list.
            INSERT INTO zone_score_kinds VALUES
                (1, 1, 3, 't', 'Third', 0, 100, 't', 't', 'L', 't', 'f', 'f', 'f', 'f'),
                (2, 1, 1, 't', 'First', 0, 100, 't', 't', 'L', 't', 'f', 'f', 'f', 'f');
            INSERT INTO zone_score_levels VALUES (1, 1, 0, 0, 0), (2, 2, 0, 0, 0);
            """;
        command.ExecuteNonQuery();
    }

    [Test]
    public async Task TheListIsPublishedInContentOrderNotKindIdOrder()
    {
        FactionScoringGameData.Instance.Load(Connection);
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishZoneScoreList(FactionScoringGameData.Instance, [
            new ZoneScoreRuntimeEntry(1, 5, 40, 0),
            new ZoneScoreRuntimeEntry(2, 5, 60, 0)
        ]);

        var body = ((SCZoneScoreListPacket)sent[0]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(2);
        expected.Write(2u);
        expected.Write(60);
        expected.Write(1u);
        expected.Write(40);

        await Assert.That(sent).HasCount(1);
        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task AScoreBeyondTheWireRangeIsClampedRatherThanWrapping()
    {
        FactionScoringGameData.Instance.Load(Connection);
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishZoneScoreList(FactionScoringGameData.Instance, [
            new ZoneScoreRuntimeEntry(1, 5, long.MaxValue, 0)
        ]);

        var body = ((SCZoneScoreListPacket)sent[0]).Write(new PacketStream()).GetBytes();
        var expected = new PacketStream();
        expected.Write(1);
        expected.Write(1u);
        expected.Write(int.MaxValue);

        await Assert.That(Hex.Of(body)).IsEqualTo(Hex.Of(expected.GetBytes()));
    }

    [Test]
    public async Task AnEntryWhoseKindNoLongersLoadsIsRefusedRatherThanDropped()
    {
        FactionScoringGameData.Instance.Load(Connection);
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        await Assert.That(() => notifier.PublishZoneScoreList(
                FactionScoringGameData.Instance,
                [new ZoneScoreRuntimeEntry(999, 5, 10, 0)]))
            .Throws<KeyNotFoundException>();
        await Assert.That(sent).IsEmpty();
    }

    [Test]
    public async Task AZoneGroupWithNoEntriesPublishesNothing()
    {
        FactionScoringGameData.Instance.Load(Connection);
        var sent = new List<GamePacket>();
        var notifier = new FactionScoringNotifier(sent.Add);

        notifier.PublishZoneScoreList(FactionScoringGameData.Instance, []);

        await Assert.That(sent).IsEmpty();
    }
}
