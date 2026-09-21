using System.Runtime.CompilerServices;
using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Indun;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Zones;
using AAEmu.UnitTests.Utils;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

[NotInParallel]
public class MentoringQuestRestorationTests
{
    [Test]
    [Arguments(6083u, 184u, 18, 29)]
    [Arguments(6084u, 184u, 30, 0)]
    [Arguments(6087u, 168u, 30, 39)]
    [Arguments(6088u, 168u, 40, 0)]
    [Arguments(6166u, 170u, 18, 29)]
    [Arguments(6167u, 170u, 30, 0)]
    [Arguments(6168u, 169u, 40, 0)]
    [Arguments(6169u, 169u, 30, 39)]
    public async Task AuthoredLevelBand_SelectsExactlyTheEligibleRole(
        uint questId,
        uint zoneId,
        int minimumLevel,
        int maximumLevel)
    {
        var template = CreateTemplate(questId, zoneId, (byte)minimumLevel);

        await Assert.That(CanStartAtLevel(template, zoneId, minimumLevel - 1, maximumLevel)).IsFalse();
        await Assert.That(CanStartAtLevel(template, zoneId, minimumLevel, maximumLevel)).IsTrue();

        if (maximumLevel > 0)
        {
            await Assert.That(CanStartAtLevel(template, zoneId, maximumLevel, maximumLevel)).IsTrue();
            await Assert.That(CanStartAtLevel(template, zoneId, maximumLevel + 1, maximumLevel)).IsFalse();
        }
        else
        {
            await Assert.That(CanStartAtLevel(template, zoneId, 125, maximumLevel)).IsTrue();
        }
    }

    [Test]
    public async Task EntryGate_RejectsOverworldZoneKeyUnrelatedZoneAndUnrelatedQuest()
    {
        var template = CreateTemplate(6083, 184, 18);
        var character = CreateCharacter(18);
        Func<QuestComponentTemplate, bool> requirements = _ => true;

        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 184, false, false, false, requirements)).IsFalse();
        // Transform.ZoneId is zones.zone_key (262 here), while quest_contexts.zone_id is zones.id (184).
        // Production resolves the key through ZoneManager before it reaches this rule.
        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 262, true, false, false, requirements)).IsFalse();
        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 170, true, false, false, requirements)).IsFalse();

        template.Id = 9999;
        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 184, true, false, false, requirements)).IsFalse();
    }

    [Test]
    public async Task ReentryAndReconnect_DoNotDuplicateCurrentOrCompletedQuest()
    {
        var template = CreateTemplate(6088, 168, 40);
        var character = CreateCharacter(40);
        Func<QuestComponentTemplate, bool> requirements = _ => true;

        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 168, true, true, false, requirements)).IsFalse();
        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 168, true, false, true, requirements)).IsFalse();
        await Assert.That(MentoringQuestRestoration.CanStartOnDungeonEntry(
            template, character, 168, true, false, false, requirements)).IsTrue();
    }

    [Test]
    public async Task RestoredSet_ContainsAllFourAuthoredPairsOnly()
    {
        await Assert.That(MentoringQuestRestoration.QuestIds)
            .IsEquivalentTo(new uint[] { 6083, 6084, 6087, 6088, 6166, 6167, 6168, 6169 });
    }

    [Test]
    public async Task DungeonEntry_ResolvesRuntimeZoneKeyBeforeSelectingQuestContext()
    {
        var template = CreateTemplate(6166, 170, 18);
        var character = CreateCharacter(18);
        PlaceInDungeon(character, 240);
        character.Quests = new CharacterQuests(character);

        var zoneManager = Mock.Of<IZoneManager>();
        zoneManager.GetZoneByKey(240).Returns(new Zone { Id = 170, ZoneKey = 240, GroupId = 47 });
        var questManager = Mock.Of<IQuestManager>();
        questManager.GetRestoredMentoringQuests(170).Returns([template]);
        uint startedQuestId = 0;

        var started = character.Quests.TryStartRestoredMentoringQuestOnDungeonEntry(
            zoneManager.Object,
            questManager.Object,
            _ => true,
            (questId, _, _) =>
            {
                startedQuestId = questId;
                return true;
            });

        await Assert.That(started).IsTrue();
        await Assert.That(startedQuestId).IsEqualTo(6166u);
        zoneManager.GetZoneByKey(240).WasCalled(Times.Once);
        questManager.GetRestoredMentoringQuests(170).WasCalled(Times.Once);
    }

    [Test]
    public async Task DungeonEntry_UsesActualMenteeRequirementAndQuestStateGuards()
    {
        var template = CreateTemplate(6166, 170, 18);
        var character = CreateCharacter(30);
        PlaceInDungeon(character, 240);
        character.Quests = new CharacterQuests(character);
        var zoneManager = Mock.Of<IZoneManager>();
        zoneManager.GetZoneByKey(240).Returns(new Zone { Id = 170, ZoneKey = 240, GroupId = 47 });
        var questManager = Mock.Of<IQuestManager>();
        questManager.GetRestoredMentoringQuests(170).Returns([template]);
        var requirements = CreateMaximumLevelRequirements(template, 29);
        var startCount = 0;

        var aboveMenteeBand = character.Quests.TryStartRestoredMentoringQuestOnDungeonEntry(
            zoneManager.Object,
            questManager.Object,
            component => requirements.CanComponentRun(component, character),
            (_, _, _) =>
            {
                startCount++;
                return true;
            });

        character.Level = 29;
        character.Quests.ActiveQuests[template.Id] =
            (Quest)RuntimeHelpers.GetUninitializedObject(typeof(Quest));
        var alreadyActive = character.Quests.TryStartRestoredMentoringQuestOnDungeonEntry(
            zoneManager.Object,
            questManager.Object,
            component => requirements.CanComponentRun(component, character),
            (_, _, _) =>
            {
                startCount++;
                return true;
            });

        character.Quests.ActiveQuests.Remove(template.Id);
        character.Quests.SetCompletedQuestFlag(template.Id, true);
        var completedToday = character.Quests.TryStartRestoredMentoringQuestOnDungeonEntry(
            zoneManager.Object,
            questManager.Object,
            component => requirements.CanComponentRun(component, character),
            (_, _, _) =>
            {
                startCount++;
                return true;
            });

        await Assert.That(aboveMenteeBand).IsFalse();
        await Assert.That(alreadyActive).IsFalse();
        await Assert.That(completedToday).IsFalse();
        await Assert.That(startCount).IsEqualTo(0);
    }

    [Test]
    public async Task MissedDailyResetAtLogin_MakesNonRepeatableMentoringQuestEligibleAgain()
    {
        var template = CreateTemplate(6166, 170, 18);
        template.Repeatable = false;
        var questManager = new QuestManager(
            Mock.Of<ITaskManager>().Object,
            Mock.Of<IZoneManager>().Object);
        var templates = (Dictionary<uint, QuestTemplate>)typeof(QuestManager)
            .GetField("_questTemplates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(questManager)!;
        templates.Add(template.Id, template);
        using var questManagerScope = new SingletonScope<QuestManager>(questManager);

        var character = CreateCharacter(18);
        character.LeaveTime = DateTime.UtcNow.AddDays(-1);
        character.Quests = new CharacterQuests(character);
        character.Quests.SetCompletedQuestFlag(template.Id, true);

        character.Quests.CheckDailyResetAtLogin();

        await Assert.That(character.Quests.HasQuestCompleted(template.Id)).IsFalse();

        PlaceInDungeon(character, 240);
        var zoneManager = Mock.Of<IZoneManager>();
        zoneManager.GetZoneByKey(240).Returns(new Zone { Id = 170, ZoneKey = 240, GroupId = 47 });
        var requirements = CreateMaximumLevelRequirements(template, 29);
        var started = character.Quests.TryStartRestoredMentoringQuestOnDungeonEntry(
            zoneManager.Object,
            questManager,
            component => requirements.CanComponentRun(component, character),
            (_, _, _) => true);

        await Assert.That(started).IsTrue();
    }

    private static bool CanStartAtLevel(
        QuestTemplate template,
        uint zoneId,
        int level,
        int maximumLevel)
    {
        var character = CreateCharacter(level);
        var requirements = CreateMaximumLevelRequirements(template, maximumLevel);
        return MentoringQuestRestoration.CanStartOnDungeonEntry(
            template,
            character,
            zoneId,
            true,
            false,
            false,
            component => requirements.CanComponentRun(component, character));
    }

    private static UnitRequirementsGameData CreateMaximumLevelRequirements(
        QuestTemplate template,
        int maximumLevel)
    {
        var gameData = new UnitRequirementsGameData();
        var requirements = maximumLevel > 0
            ? new List<UnitReqs>
            {
                new()
                {
                    OwnerType = "QuestComponent",
                    OwnerId = template.GetComponents(QuestComponentKind.Start).Single().Id,
                    KindType = UnitReqsKindType.MaxLevel,
                    Value1 = (uint)maximumLevel
                }
            }
            : [];
        typeof(UnitRequirementsGameData)
            .GetProperty("_unitReqsByOwnerType", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(gameData, new Dictionary<string, List<UnitReqs>>
            {
                ["QuestComponent"] = requirements
            });
        return gameData;
    }

    private static QuestTemplate CreateTemplate(uint questId, uint zoneId, byte minimumLevel)
    {
        var template = new QuestTemplate
        {
            Id = questId,
            ZoneId = zoneId,
            DetailId = QuestDetail.Daily,
            MinLevel = minimumLevel,
            MaxLevel = 0,
            RaceMask = byte.MaxValue
        };
        var start = new QuestComponentTemplate(template)
        {
            Id = questId + 20_000,
            KindId = QuestComponentKind.Start
        };
        template.Components.Add(start.Id, start);
        return template;
    }

    private static Character CreateCharacter(int level) =>
        new(new UnitCustomModelParams())
        {
            Id = 42,
            Name = "MentoringTester",
            Level = (byte)level,
            Race = Race.Nuian
        };

    private static WorldInstance CreateDungeonWorld(uint zoneKey)
    {
        var world = new WorldInstance(
            new WorldTemplate { Name = "mentoring_test", ZoneKeys = [zoneKey] },
            channelId: 1,
            dontFreeInstanceId: true,
            instanceId: 42);
        world.DungeonInstance = (Dungeon)RuntimeHelpers.GetUninitializedObject(typeof(Dungeon));
        return world;
    }

    private static void PlaceInDungeon(Character character, uint zoneKey)
    {
        // Avoid the runtime transfer callbacks: this fixture establishes the already-loaded state that
        // CSInstanceLoadedPacket has when it invokes the restoration hook.
        character.Transform.KeepZoneQuietly(zoneKey);
        typeof(GameObject)
            .GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, CreateDungeonWorld(zoneKey));
    }
}
