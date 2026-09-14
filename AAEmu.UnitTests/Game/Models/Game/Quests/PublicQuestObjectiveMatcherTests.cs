using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Expeditions;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Acts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class PublicQuestObjectiveMatcherTests
{
    [Test]
    public async Task ExactSupportedObjectives_ReturnContentIndexDeltaAndTarget()
    {
        var template = new QuestTemplate { Id = 7001, DetailId = QuestDetail.Expedition };
        var component = new QuestComponentTemplate(template) { KindId = QuestComponentKind.Progress };
        var character = new Character(new UnitCustomModelParams())
        {
            Id = 10,
            Expedition = new Expedition { Id = (FactionsEnum)1 }
        };
        var quests = Mock.Of<IQuestManager>();
        quests.CheckGroupItem(88, 9001).Returns(true);
        quests.CheckContextGroup(42, 8001).Returns(true);
        var npc = new Npc
        {
            Level = 30,
            Template = new NpcTemplate { NpcGradeId = NpcGradeType.Normal }
        };
        var cases = new (QuestActTemplate Objective, EventArgs Source, int Delta, int Target)[]
        {
            (Objective(new QuestActObjConsumeEvolvingMaterial(component), 700),
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.EvolvingMaterial, Amount = 4 }, 4, 700),
            (Objective(new QuestActObjGainHonorPoint(component), 120000),
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Honor, Amount = 5 }, 5, 120000),
            (Objective(new QuestActObjGainLivingPoint(component), 120000),
                new OnQuestProgressStatArgs { Kind = QuestProgressStatKind.Living, Amount = 6 }, 6, 120000),
            (Objective(new QuestActObjLaborPower(component), 180000),
                new OnLaborPowerArgs { LaborUsed = 7, ActabilityGroupId = 3 }, 7, 180000),
            (Objective(new QuestActObjNpcKill(component)
                {
                    LevelMin = 30,
                    HeirLevelMax = 55,
                    GradeNormal = true,
                    GradeStrong = true,
                    GradeElite = true
                }, 18000), new OnZoneKillArgs { Victim = npc }, 1, 18000),
            (Objective(new QuestActObjMonsterGroupHunt(component) { QuestMonsterGroupId = 679 }, 100),
                new OnMonsterGroupHuntArgs { NpcId = 679, Count = 8 }, 8, 100),
            (Objective(new QuestActObjItemGroupUse(component) { ItemGroupId = 88 }, 1200),
                new OnItemUseArgs { ItemId = 9001 }, 1, 1200),
            (Objective(new QuestActObjCompleteQuestGroup(component) { QuestContextGroupId = 42 }, 300),
                new OnQuestCompleteArgs { QuestId = 8001 }, 1, 300)
        };

        foreach (var testCase in cases)
        {
            await Assert.That(PublicQuestProgressEvent.TryCapture(character, testCase.Source, out var captured))
                .IsTrue();
            var matched = PublicQuestObjectiveMatcher.TryGetProgress(
                testCase.Objective, captured, quests.Object, out var progress);
            await Assert.That(matched).IsTrue();
            await Assert.That(progress).IsEqualTo(
                new PublicQuestObjectiveProgress(2, testCase.Delta, testCase.Target));
        }
    }

    [Test]
    public async Task MismatchedContentEvent_ReturnsNoProgress()
    {
        var template = new QuestTemplate { Id = 7001, DetailId = QuestDetail.Expedition };
        var component = new QuestComponentTemplate(template) { KindId = QuestComponentKind.Progress };
        var objective = Objective(new QuestActObjGainHonorPoint(component), 120000);
        var progressEvent = new PublicQuestProgressEvent(
            10, 1, PublicQuestProgressEventKind.Living, 10, 0, 0, 0, 0, default);

        var matched = PublicQuestObjectiveMatcher.TryGetProgress(
            objective, progressEvent, Mock.Of<IQuestManager>().Object, out _);

        await Assert.That(matched).IsFalse();
    }

    [Test]
    public async Task NpcCapture_RemainsStableAfterMutableNpcChanges()
    {
        var template = new QuestTemplate { Id = 7001, DetailId = QuestDetail.Expedition };
        var component = new QuestComponentTemplate(template) { KindId = QuestComponentKind.Progress };
        var objective = Objective(new QuestActObjNpcKill(component)
        {
            LevelMin = 30,
            HeirLevelMax = 55,
            GradeNormal = true
        }, 18000);
        var character = new Character(new UnitCustomModelParams())
        {
            Id = 10,
            Expedition = new Expedition { Id = (FactionsEnum)1 }
        };
        var npc = new Npc
        {
            Level = 30,
            Template = new NpcTemplate { NpcGradeId = NpcGradeType.Normal }
        };
        await Assert.That(PublicQuestProgressEvent.TryCapture(
            character, new OnZoneKillArgs { Victim = npc }, out var captured)).IsTrue();

        npc.Level = 1;
        npc.Template.NpcGradeId = NpcGradeType.BossA;

        await Assert.That(PublicQuestObjectiveMatcher.TryGetProgress(
            objective, captured, Mock.Of<IQuestManager>().Object, out var progress)).IsTrue();
        await Assert.That(progress.Delta).IsEqualTo(1);
    }

    private static T Objective<T>(T objective, int target) where T : QuestActTemplate
    {
        objective.Count = target;
        objective.ThisComponentObjectiveIndex = 2;
        return objective;
    }
}
