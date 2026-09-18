using System.Reflection;

using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.UnitTests.Utils;

using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Models.Game.Achievement;

/// <summary>
/// The manager against seeded content: what a record report does to the achievements watching it, what
/// completion chains into, and what the client is sent.
/// </summary>
[NotInParallel]
public sealed class AchievementManagerTests : SqliteTestBase
{
    private const uint AbilityRecord = 100;
    private const uint HouseRecordA = 200;
    private const uint HouseRecordB = 201;
    private const uint ArmorRecord = 202;
    private const uint FirstCompletionRecord = 300;
    private const uint SecondCompletionRecord = 301;
    private const uint KillRecord = 400;
    private const uint LevelRecord = 500;
    private const uint AbilityLevelRecord = 501;
    private const uint OtherAbilityLevelRecord = 502;
    private const uint ChainRecord = 760;
    private const uint ChainCompletionRecord = 761;

    /// <summary>How many achievements the seeded content holds: ids 1 to 7 and the chain pair below.</summary>
    private const int ContentAchievements = 9;

    /// <summary>Enough spare content that a full list needs more than one packet of fifty.</summary>
    private const int FillerAchievements = 60;

    private readonly List<byte[]> _sentPackets = [];
    private Character _character;
    private SingletonScope<ExperienceManager> _experience;

    protected override void CreateTestSchema()
    {
        base.CreateTestSchema();
        Execute("CREATE TABLE achievements (id INTEGER PRIMARY KEY, name TEXT, summary TEXT, description TEXT, " +
                "achievement_sub_category_id INTEGER, parent_achievement_id INTEGER, season_off TEXT, " +
                "is_hidden TEXT, priority INTEGER, or_unit_reqs TEXT, complete_or TEXT, complete_num INTEGER, " +
                "item_id INTEGER, icon_id INTEGER, item_num INTEGER, grade_id INTEGER, appellation_id INTEGER, " +
                "milestone_id INTEGER)");
        Execute("CREATE TABLE achievement_objectives (id INTEGER PRIMARY KEY, achievement_id INTEGER, " +
                "or_unit_reqs TEXT, record_id INTEGER)");
        Execute("CREATE TABLE char_records (id INTEGER PRIMARY KEY, kind_id INTEGER, value1 INTEGER, value2 INTEGER)");
        Execute("CREATE TABLE pre_completed_achievements (id INTEGER PRIMARY KEY, " +
                "completed_achievement_id INTEGER, my_achievement_id INTEGER)");
    }

    [Before(Test)]
    public void Before()
    {
        SeedContent();
        var gameData = new AchievementGameData();
        gameData.Load(Connection);
        gameData.PostLoad();

        ResetSingleton<AchievementGameData>();
        ResetSingleton<AchievementManager>();
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(gameData)
            .AddSingleton(new AchievementManager())
            .BuildServiceProvider();

        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        var connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams()) { Id = 42, Name = "Achiever" };
        _character.Connection = connection;
        connection.ActiveChar = _character;
        _character.Records = new CharacterRecords(_character);
        _character.Achievements = new CharacterAchievements(_character);

        // Ability levels are read through the experience tables, so the real manager is scoped with a small
        // deterministic table rather than left uninitialised.
        _experience = new SingletonScope<ExperienceManager>(CreateExperienceManager());
    }

    [After(Test)]
    public void After()
    {
        _experience?.Dispose();
        SingletonContainer.ServiceProvider = null;
        ResetSingleton<AchievementGameData>();
        ResetSingleton<AchievementManager>();
    }

    [Test]
    public async Task Report_MovesTheAchievementAndTellsTheClient()
    {
        // Achievement 1: "reach ability 50", complete_num 50 with one objective on the ability record.
        await Assert.That(AchievementManager.Instance.Report(_character, AbilityRecord, 49)).IsEqualTo(1);
        await Assert.That(_character.Achievements.Amount(1)).IsEqualTo(49);
        await Assert.That(_character.Achievements.IsComplete(1)).IsFalse();

        // Reaching the target completes it, and that completion is itself a record the achievement built on
        // it watches, so two achievements moved.
        await Assert.That(AchievementManager.Instance.Report(_character, AbilityRecord, 50)).IsEqualTo(2);
        await Assert.That(_character.Achievements.Amount(1)).IsEqualTo(50);
        await Assert.That(_character.Achievements.IsComplete(1)).IsTrue();
        await Assert.That(_character.Achievements.CompletedAt(1)).IsNotNull();
        // One change packet per report, the completion, and the change the completion record makes to the
        // achievement built on it.
        await Assert.That(_sentPackets.Count).IsEqualTo(4);
    }

    [Test]
    public async Task Report_KeepsTheHighestValueAndStaysQuietWhenNothingMoved()
    {
        AchievementManager.Instance.Report(_character, AbilityRecord, 50);
        _sentPackets.Clear();

        await Assert.That(AchievementManager.Instance.Report(_character, AbilityRecord, 10)).IsEqualTo(0);
        await Assert.That(_character.Records.Get(AbilityRecord)).IsEqualTo(50);
        await Assert.That(_character.Achievements.Amount(1)).IsEqualTo(50);
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task CountingAchievement_CompletesOnOneOfItsObjectives()
    {
        // Achievement 2: "complete one of these houses", complete_num 1 over three housing records.
        AchievementManager.Instance.Report(_character, HouseRecordB, 1);

        await Assert.That(_character.Achievements.Amount(2)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(2)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(3)).IsFalse();
    }

    [Test]
    public async Task Completion_ChainsThroughTheCompletionRecord()
    {
        // Achievement 3 is earned by earning achievements 1 and 2: its objectives are the records that count
        // their completions, so a completion has to reach it two levels up.
        AchievementManager.Instance.Report(_character, AbilityRecord, 50);

        await Assert.That(_character.Achievements.IsComplete(1)).IsTrue();
        await Assert.That(_character.Records.Get(FirstCompletionRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.Amount(3)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(3)).IsFalse();

        AchievementManager.Instance.Report(_character, HouseRecordA, 1);

        await Assert.That(_character.Records.Get(SecondCompletionRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.Amount(3)).IsEqualTo(2);
        await Assert.That(_character.Achievements.IsComplete(3)).IsTrue();
    }

    [Test]
    public async Task AchievementWithoutObjectives_IsNeverCompletedByAReport()
    {
        AchievementManager.Instance.Report(_character, AbilityRecord, 50);
        AchievementManager.Instance.RefreshAll(_character);

        await Assert.That(_character.Achievements.IsComplete(4)).IsFalse();
    }

    [Test]
    public async Task BuildList_ReportsOnlyWhatTheCharacterHasTouched()
    {
        AchievementManager.Instance.Report(_character, AbilityRecord, 50);

        var touched = AchievementManager.Instance.BuildList(_character);
        await Assert.That(touched.Count).IsEqualTo(2);
        await Assert.That(touched[0].Id).IsEqualTo(1u);
        await Assert.That(touched[0].Amount).IsEqualTo(50u);
        await Assert.That(touched[0].Complete).IsNotEqualTo(default(DateTime));
        // The achievement earned by earning this one has moved too, through its completion record.
        await Assert.That(touched[1].Id).IsEqualTo(3u);
        await Assert.That(touched[1].Amount).IsEqualTo(1u);
        await Assert.That(touched[1].Complete).IsEqualTo(default(DateTime));

        // The full list carries everything at nothing, which is what the client starts from.
        var all = AchievementManager.Instance.BuildList(_character, includeUntouched: true);
        await Assert.That(all.Count).IsEqualTo(ContentAchievements + FillerAchievements);
        await Assert.That(all.Single(row => row.Id == 4u).Amount).IsEqualTo(0u);
        await Assert.That(all.Single(row => row.Id == 4u).Complete).IsEqualTo(default(DateTime));
    }

    [Test]
    public async Task RefreshAll_RevisitsAWatcherWhoseChildCompletesLaterInThePass()
    {
        // Achievement 30 is earned by earning achievement 31, and the full pass walks them in id order: 30 is
        // checked first, before 31 has completed, so the watcher has to be looked at again when it does.
        // Otherwise 30 goes out stale and only catches up at the next world entry.
        _character.Records.Set(ChainRecord, 3);

        await Assert.That(AchievementManager.Instance.RefreshAll(_character)).IsEqualTo(2);
        await Assert.That(_character.Achievements.IsComplete(31)).IsTrue();
        await Assert.That(_character.Records.Get(ChainCompletionRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.Amount(30)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(30)).IsTrue();
    }

    [Test]
    public async Task RefreshAll_LeavesAChainThatIsStillIncompleteAlone()
    {
        // The other half of the same walk: re-queueing is not "complete everything that watches a record".
        _character.Records.Set(ChainRecord, 2);

        await Assert.That(AchievementManager.Instance.RefreshAll(_character)).IsEqualTo(0);
        await Assert.That(_character.Achievements.IsComplete(31)).IsFalse();
        await Assert.That(_character.Records.Get(ChainCompletionRecord)).IsEqualTo(0);
        await Assert.That(_character.Achievements.IsComplete(30)).IsFalse();
    }

    [Test]
    public async Task SendList_SplitsTheListIntoTakablePackets()
    {
        // Everything watching the kill record carries progress, which is more than one packet may hold.
        AchievementManager.Instance.Report(_character, KillRecord, 1);

        var rows = AchievementManager.Instance.BuildList(_character);
        await Assert.That(rows.Count).IsEqualTo(1 + FillerAchievements);

        _sentPackets.Clear();
        await Assert.That(AchievementManager.Instance.SendList(_character)).IsEqualTo(2);
        await Assert.That(_sentPackets.Count).IsEqualTo(2);
    }

    [Test]
    public async Task RefreshAll_CompletesWhatTheRecordsAlreadySayWithoutSendingAnything()
    {
        // Records can be ahead of the stored progress (they outlive a session, and a report can arrive while
        // the character is offline); the entry path resolves that before it pushes the list.
        _character.Records.Set(AbilityRecord, 50);
        _sentPackets.Clear();

        await Assert.That(AchievementManager.Instance.RefreshAll(_character)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(1)).IsTrue();
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    [Test]
    public async Task ForcedCompletionAndReset_MoveTheCharacter()
    {
        await Assert.That(AchievementManager.Instance.Complete(_character, 2)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(2)).IsTrue();
        // Forced completion is a record like any other, so it chains.
        await Assert.That(_character.Achievements.Amount(3)).IsEqualTo(1);
        await Assert.That(AchievementManager.Instance.Complete(_character, 2)).IsFalse();

        await Assert.That(AchievementManager.Instance.Reset(_character, 2)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(2)).IsFalse();
        await Assert.That(_character.Achievements.Amount(2)).IsEqualTo(0);

        await Assert.That(AchievementManager.Instance.Complete(_character, 9999)).IsFalse();
        await Assert.That(AchievementManager.Instance.Reset(_character, 9999)).IsFalse();
    }

    [Test]
    public async Task Reset_KeepsACompletionRecordWhoseAchievementIsStillComplete()
    {
        // Achievement 30 watches 31's completion. After both are earned, resetting 30 must leave 31's
        // record — a complete child never reports again, so clearing it would make 30 unearnable.
        _character.Records.Set(ChainRecord, 3);
        AchievementManager.Instance.RefreshAll(_character);
        await Assert.That(_character.Achievements.IsComplete(30)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(31)).IsTrue();

        await Assert.That(AchievementManager.Instance.Reset(_character, 30)).IsTrue();
        await Assert.That(_character.Achievements.IsComplete(30)).IsFalse();
        await Assert.That(_character.Records.Get(ChainCompletionRecord)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(31)).IsTrue();

        await Assert.That(AchievementManager.Instance.RefreshAll(_character)).IsEqualTo(1);
        await Assert.That(_character.Achievements.IsComplete(30)).IsTrue();
    }

    [Test]
    public async Task Reset_ClearsTheRecordsThatWouldOtherwisePutItStraightBack()
    {
        AchievementManager.Instance.Report(_character, HouseRecordB, 1);
        await Assert.That(_character.Achievements.IsComplete(2)).IsTrue();
        await Assert.That(_character.Records.Get(HouseRecordB)).IsEqualTo(1);
        _sentPackets.Clear();

        await Assert.That(AchievementManager.Instance.Reset(_character, 2)).IsTrue();
        // Every record the achievement is judged by goes with it, and the client hears only the reset.
        await Assert.That(_character.Records.Get(HouseRecordA)).IsEqualTo(0);
        await Assert.That(_character.Records.Get(HouseRecordB)).IsEqualTo(0);
        await Assert.That(_character.Records.Get(ArmorRecord)).IsEqualTo(0);
        await Assert.That(_sentPackets.Count).IsEqualTo(1);

        // Re-evaluating everything, as world entry does, must not earn it again: the records no longer say
        // the character did any of it.
        await Assert.That(AchievementManager.Instance.RefreshAll(_character)).IsEqualTo(0);
        await Assert.That(_character.Achievements.IsComplete(2)).IsFalse();
        await Assert.That(_character.Achievements.Amount(2)).IsEqualTo(0);
    }

    [Test]
    public async Task UnknownRecords_AreIgnored()
    {
        await Assert.That(AchievementManager.Instance.Report(_character, 9999, 5)).IsEqualTo(0);
        await Assert.That(AchievementManager.Instance.SetRecord(_character, 9999, 5)).IsEqualTo(0);
        await Assert.That(AchievementManager.Instance.SetRecord(_character, AbilityRecord, 50)).IsEqualTo(2);
    }

    [Test]
    public async Task ReportCharacterProgress_FeedsTheLevelRecordTheEngineOwns()
    {
        // A level achievement ("reach level 7") watches the one character-level record.
        _character.Level = 7;

        await Assert.That(AchievementManager.Instance.ReportCharacterProgress(_character)).IsEqualTo(1);
        await Assert.That(_character.Records.Get(LevelRecord)).IsEqualTo(7);
        await Assert.That(_character.Achievements.IsComplete(6)).IsTrue();

        // A lower level is not a new record: the store keeps the high-water mark.
        _character.Level = 6;
        await Assert.That(AchievementManager.Instance.ReportCharacterProgress(_character)).IsEqualTo(0);
        await Assert.That(_character.Records.Get(LevelRecord)).IsEqualTo(7);
    }

    [Test]
    public async Task ReportAbilityLevels_ReadsEachAbilitysOwnRecord()
    {
        // An ability-level record names its ability in value1, so the record for an ability the character
        // holds reads that ability's level and the records for abilities it does not hold stay at nothing.
        var abilities = new CharacterAbilities(_character);
        _character.Abilities = abilities;
        abilities.Abilities[AbilityType.Fight].Exp = 300;   // level 3 in this test's experience table

        await Assert.That(AchievementManager.Instance.ReportAbilityLevels(_character)).IsEqualTo(1);
        await Assert.That(_character.Records.Get(AbilityLevelRecord)).IsEqualTo(3);
        await Assert.That(_character.Achievements.IsComplete(7)).IsTrue();
        await Assert.That(_character.Records.Get(OtherAbilityLevelRecord)).IsEqualTo(0);
    }

    [Test]
    public async Task LevelReporting_ToleratesACharacterWithoutAbilities()
    {
        _character.Abilities = null;
        _character.Level = 7;

        await Assert.That(AchievementManager.Instance.ReportAbilityLevels(_character)).IsEqualTo(0);
        await Assert.That(AchievementManager.Instance.ReportLevel(_character)).IsEqualTo(1);
    }

    private void SeedContent()
    {
        // Achievement 1: summing shape, single objective, target 50.
        InsertAchievement(1, 50, "f", "Reach ability level 50");
        InsertObjective(1, 1, AbilityRecord);

        // Achievement 2: counting shape, one of three houses.
        InsertAchievement(2, 1, "t", "Complete one of these houses");
        InsertObjective(2, 2, HouseRecordA);
        InsertObjective(2, 3, HouseRecordB);
        InsertObjective(2, 4, ArmorRecord);

        // Achievement 3: earned by earning achievements 1 and 2, through their completion records.
        InsertAchievement(3, 0, "t", "Earn both");
        InsertObjective(3, 5, FirstCompletionRecord);
        InsertObjective(3, 6, SecondCompletionRecord);

        // Achievement 4: no objectives at all.
        InsertAchievement(4, 0, "t", "Nothing counts towards this");

        // Achievement 5: untouched filler so the full list is more than the touched set.
        InsertAchievement(5, 10, "f", "Kill ten");
        InsertObjective(5, 7, KillRecord);

        // Achievement 6: reach level 7, off the one character-level record the engine reports.
        InsertAchievement(6, 7, "f", "Reach level 7");
        InsertObjective(6, 8, LevelRecord);

        // Achievement 7: reach Fight level 3, off that ability's record.
        InsertAchievement(7, 3, "f", "Reach Fight level 3");
        InsertObjective(7, 9, AbilityLevelRecord);

        // Achievements 30 and 31: a chain whose parent is the lower id, the way 2,173 of the content's 3,259
        // completion links run. The parent is checked before the child that completes it.
        InsertAchievement(30, 1, "t", "Earn the chain child");
        InsertObjective(30, 20, ChainCompletionRecord);
        InsertAchievement(31, 3, "f", "Reach the chain target");
        InsertObjective(31, 21, ChainRecord);

        // Spare counting achievements on the same record: each is complete on its first kill.
        for (var i = 0; i < FillerAchievements; i++)
        {
            var id = (uint)(2000 + i);
            InsertAchievement(id, 1, "t", $"Kill one of kind {i}");
            InsertObjective(id, (uint)(100 + i), KillRecord);
        }

        InsertRecord(AbilityRecord, 21, 0, 0);              // ability level
        InsertRecord(HouseRecordA, 38, 1, -1);              // housing
        InsertRecord(HouseRecordB, 38, 2, -1);
        InsertRecord(ArmorRecord, 29, 3, -1);               // item type
        InsertRecord(FirstCompletionRecord, 9, 1, 0);       // completing achievement 1
        InsertRecord(SecondCompletionRecord, 9, 2, 0);      // completing achievement 2
        InsertRecord(KillRecord, 25, 5, 0);                 // npc kills
        InsertRecord(LevelRecord, 10, 0, 0);                // character level
        InsertRecord(AbilityLevelRecord, 21, 1, 0);         // Fight
        InsertRecord(OtherAbilityLevelRecord, 21, 2, 0);    // Illusion
        InsertRecord(ChainRecord, 25, 12, 0);               // the chain child's own objective
        InsertRecord(ChainCompletionRecord, 9, 31, 0);      // completing achievement 31
    }

    private void InsertAchievement(uint id, int completeNum, string completeOr, string name) =>
        Execute($"INSERT INTO achievements (id, name, summary, description, achievement_sub_category_id, " +
                $"parent_achievement_id, season_off, is_hidden, priority, or_unit_reqs, complete_or, " +
                $"complete_num, item_id, icon_id, item_num, grade_id, appellation_id, milestone_id) " +
                $"VALUES ({id}, '{name}', '', '', 1, 0, 'f', 'f', 0, 'f', '{completeOr}', {completeNum}, " +
                $"0, 0, 0, 0, 0, 0)");

    private void InsertObjective(uint achievementId, uint objectiveId, uint recordId) =>
        Execute($"INSERT INTO achievement_objectives (id, achievement_id, or_unit_reqs, record_id) " +
                $"VALUES ({objectiveId}, {achievementId}, 'f', {recordId})");

    private void InsertRecord(uint id, int kind, int value1, int value2) =>
        Execute($"INSERT INTO char_records (id, kind_id, value1, value2) VALUES ({id}, {kind}, {value1}, {value2})");

    private void Execute(string sql)
    {
        using var command = Connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static ExperienceManager CreateExperienceManager()
    {
        var manager = new ExperienceManager();
        var loader = Mock.Of<IExperienceLevelTemplateLoader>();
        loader.Load().Returns(Enumerable.Range(1, 60)
            .Select(level => new ExperienceLevelTemplate
            {
                Level = (byte)level,
                TotalExp = level * 100,
                TotalMateExp = level * 50,
                SkillPoints = level * 10
            })
            .ToArray());
        manager.Load(loader.Object, 60, 60);
        return manager;
    }

    private static void ResetSingleton<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)
            ?.SetValue(null, null);
}
