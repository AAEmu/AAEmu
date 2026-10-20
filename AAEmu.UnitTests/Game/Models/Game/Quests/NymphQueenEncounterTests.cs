using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Units;

using Microsoft.Extensions.Time.Testing;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class NymphQueenEncounterTests
{
    [Test]
    public async Task QuestRemoved_AbandonedOrRemovedByGm_ResetsAndDoesNotFailReplacementQuest()
    {
        var owner = CreatePlayer(1);
        var spawner = new TestSpawner();
        var original = new Npc { TemplateId = 9385, Spawner = spawner };
        var clock = new FakeTimeProvider();
        var quests = Mock.Of<IQuestManager>();
        var encounter = new NymphQueenEncounter(original, owner, [], quests.Object,
            Mock.Of<ITaskManager>().Object, clock, new());
        encounter.TryAccept(owner, () => AddQuest(owner, quests.Object));
        var previousQuest = owner.Quests.ActiveQuests[2008];

        encounter.QuestRemoved(owner, previousQuest);
        AddQuest(owner, quests.Object);
        encounter.QuestRemoved(owner, previousQuest);
        clock.Advance(TimeSpan.FromSeconds(5));
        encounter.Tick();

        await Assert.That(spawner.Removals).IsEqualTo(1);
        await Assert.That(spawner.Encounter).IsNull();
        await Assert.That(owner.NymphQueenEncounter).IsNull();
        quests.FailQuest(owner, 2008).WasNeverCalled();
        await Assert.That(owner.Quests.ActiveQuests[2008].Status).IsEqualTo(QuestStatus.Progress);
    }

    [Test]
    public async Task TryAccept_OutsidePlayer_CannotDamageOrReceiveDialogueCredit()
    {
        var owner = CreatePlayer(1);
        var outsider = CreatePlayer(3);
        var original = new Npc { TemplateId = 9385, Spawner = new TestSpawner() };
        var quests = Mock.Of<IQuestManager>();
        var encounter = new NymphQueenEncounter(original, owner, [2], quests.Object,
            Mock.Of<ITaskManager>().Object, new FakeTimeProvider(), new());
        encounter.TryAccept(owner, () => AddQuest(owner, quests.Object));

        await Assert.That(encounter.CanDamage(owner)).IsTrue();
        await Assert.That(encounter.CanDamage(outsider)).IsFalse();
        await Assert.That(encounter.TryAccept(outsider, () => AddQuest(outsider, quests.Object))).IsFalse();
        encounter.AttachReplacement(new Npc { TemplateId = 8993, Spawner = new TestSpawner() });
        await Assert.That(encounter.CanDamage(owner)).IsFalse();
        await Assert.That(encounter.CanTalk(1, 1)).IsTrue();
        await Assert.That(encounter.CanTalk(1, 3)).IsFalse();
        await Assert.That(encounter.CanTalk(3, 1)).IsFalse();
    }

    [Test]
    public async Task TryAccept_PrerequisiteRejected_ReleasesReservationWithoutRemovingNpc()
    {
        var owner = CreatePlayer(1);
        var spawner = new TestSpawner();
        var original = new Npc { TemplateId = 9385, Spawner = spawner };
        var encounter = new NymphQueenEncounter(original, owner, [], Mock.Of<IQuestManager>().Object,
            Mock.Of<ITaskManager>().Object, new FakeTimeProvider(), new());

        var accepted = encounter.TryAccept(owner, () => false);

        await Assert.That(accepted).IsFalse();
        await Assert.That(original.Encounter).IsNull();
        await Assert.That(spawner.Encounter).IsNull();
        await Assert.That(spawner.Removals).IsEqualTo(0);
    }

    [Test]
    public async Task Tick_SuccessfulConversation_RestoresSpawnerOnceWithoutFailingQuest()
    {
        var owner = CreatePlayer(1);
        var originalSpawner = new TestSpawner();
        var original = new Npc { TemplateId = 9385, Spawner = originalSpawner };
        var replacementSpawner = new TestSpawner();
        var replacement = new Npc { TemplateId = 8993, Spawner = replacementSpawner };
        var clock = new FakeTimeProvider();
        var tasks = Mock.Of<ITaskManager>();
        var quests = Mock.Of<IQuestManager>();
        var encounter = new NymphQueenEncounter(original, owner, [], quests.Object, tasks.Object, clock, new());
        encounter.TryAccept(owner, () => AddQuest(owner, quests.Object));
        encounter.AttachReplacement(replacement);
        owner.Quests.ActiveQuests[2008].Step = QuestComponentKind.Ready;

        encounter.Tick();
        await Assert.That(originalSpawner.Removals).IsEqualTo(1);
        await Assert.That(replacementSpawner.Removals).IsEqualTo(1);
        await Assert.That(originalSpawner.Encounter).IsNotNull();
        clock.Advance(TimeSpan.FromSeconds(5));
        encounter.Tick();
        encounter.Tick();

        await Assert.That(originalSpawner.Encounter).IsNull();
        await Assert.That(owner.NymphQueenEncounter).IsNull();
        await Assert.That(originalSpawner.Removals).IsEqualTo(1);
        quests.FailQuest(Any<ICharacter>(), Any<uint>()).WasNeverCalled();
        tasks.Cancel(Any<AAEmu.Game.Models.Tasks.Task>()).WasCalled(Times.Once);
    }

    [Test]
    public async Task AbandonCombat_RepeatedReturnAndDisconnect_FailsOnceAndSchedulesOneReset()
    {
        var owner = CreatePlayer(1);
        var spawner = new TestSpawner();
        var original = new Npc { TemplateId = 9385, Spawner = spawner };
        var quests = Mock.Of<IQuestManager>();
        var encounter = new NymphQueenEncounter(original, owner, [], quests.Object,
            Mock.Of<ITaskManager>().Object, new FakeTimeProvider(), new());
        encounter.TryAccept(owner, () => AddQuest(owner, quests.Object));

        encounter.AbandonCombat();
        encounter.AbandonCombat();
        encounter.OwnerUnavailable(owner);

        quests.FailQuest(owner, 2008).WasCalled(Times.Once);
        await Assert.That(owner.Quests.ActiveQuests[2008].Status).IsEqualTo(QuestStatus.Failed);
        await Assert.That(spawner.Removals).IsEqualTo(1);
        await Assert.That(encounter.CanDamage(owner)).IsFalse();
    }

    [Test]
    public async Task Tick_DialogueExpires_OnlyFailsUnfinishedPartyMembers()
    {
        var owner = CreatePlayer(1);
        var partyMember = CreatePlayer(2);
        var quests = Mock.Of<IQuestManager>();
        var clock = new FakeTimeProvider();
        var original = new Npc { TemplateId = 9385, Spawner = new TestSpawner() };
        var encounter = new NymphQueenEncounter(original, owner, [2], quests.Object,
            Mock.Of<ITaskManager>().Object, clock, new());
        encounter.TryAccept(owner, () => AddQuest(owner, quests.Object));
        encounter.TryAccept(partyMember, () => AddQuest(partyMember, quests.Object));
        encounter.AttachReplacement(new Npc { TemplateId = 8993, Spawner = new TestSpawner() });
        owner.Quests.ActiveQuests[2008].Step = QuestComponentKind.Ready;
        encounter.Tick();
        clock.Advance(TimeSpan.FromSeconds(50));

        encounter.Tick();

        quests.FailQuest(owner, 2008).WasNeverCalled();
        quests.FailQuest(partyMember, 2008).WasCalled(Times.Once);
        await Assert.That(owner.Quests.ActiveQuests[2008].Step).IsEqualTo(QuestComponentKind.Ready);
        await Assert.That(partyMember.Quests.ActiveQuests[2008].Status).IsEqualTo(QuestStatus.Failed);
    }

    [Test]
    public async Task AbandonCombat_QuestReplaced_DoesNotFailNewQuestInstance()
    {
        var owner = CreatePlayer(1);
        var quests = Mock.Of<IQuestManager>();
        var original = new Npc { TemplateId = 9385, Spawner = new TestSpawner() };
        var encounter = new NymphQueenEncounter(original, owner, [], quests.Object,
            Mock.Of<ITaskManager>().Object, new FakeTimeProvider(), new());
        encounter.TryAccept(owner, () => AddQuest(owner, quests.Object));
        AddQuest(owner, quests.Object);

        encounter.AbandonCombat();

        quests.FailQuest(owner, 2008).WasNeverCalled();
        await Assert.That(owner.Quests.ActiveQuests[2008].Status).IsEqualTo(QuestStatus.Progress);
    }

    [Test]
    public async Task RecoverInterruptedQuest_Restart_FailsCombatButPreservesFinalTurnIn()
    {
        var owner = CreatePlayer(1);
        AddQuest(owner, Mock.Of<IQuestManager>().Object);
        var quest = owner.Quests.ActiveQuests[2008];
        NymphQueenEncounter.RecoverInterruptedQuest(quest);
        await Assert.That(quest.Status).IsEqualTo(QuestStatus.Failed);
        AddQuest(owner, Mock.Of<IQuestManager>().Object);
        quest = owner.Quests.ActiveQuests[2008];
        quest.Step = QuestComponentKind.Ready;
        NymphQueenEncounter.RecoverInterruptedQuest(quest);
        await Assert.That(quest.Step).IsEqualTo(QuestComponentKind.Ready);
        await Assert.That(quest.Status).IsEqualTo(QuestStatus.Progress);
    }

    private static Character CreatePlayer(uint id)
    {
        var player = new Character(new UnitCustomModelParams()) { Id = id };
        player.Quests = new CharacterQuests(player);
        return player;
    }

    private static bool AddQuest(Character player, IQuestManager quests)
    {
        player.Quests.ActiveQuests[2008] = new Quest(null, player, quests, Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object, Mock.Of<IExpressTextManager>().Object, Mock.Of<IWorldManager>().Object)
        { TemplateId = 2008, Step = QuestComponentKind.Progress, Status = QuestStatus.Progress };
        return true;
    }

    private sealed class TestSpawner : NpcSpawner
    {
        public int Removals { get; private set; }
        public override void Despawn(Npc npc)
        {
            Removals++;
        }
    }
}
