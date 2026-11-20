using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Scripts;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class QuestScriptTests
{
    [Test]
    public async Task TryAcceptQuest_NymphScriptRejectedByCore_ReleasesReservationForRetry()
    {
        var world = new WorldInstance(new WorldTemplate { Name = "test" }, 0, true, uint.MaxValue);
        var spawner = new NpcSpawner { ParentWorld = world };
        var npc = new Npc { ObjId = 10, TemplateId = 9385, Spawner = spawner, ParentWorld = world };
        world.SetNpc(npc.ObjId, npc);
        var player = new Character(null) { ParentWorld = world, CurrentTarget = npc };
        player.Quests = new CharacterQuests(player);
        var tasks = Mock.Of<ITaskManager>().Object;
        var script = new NymphQueenQuestScript(new Lazy<IQuestManager>(() => Mock.Of<IQuestManager>().Object),
            tasks, Mock.Of<ITeamManager>().Object, TimeProvider.System, new());
        var manager = new QuestManager(tasks, Mock.Of<IZoneManager>().Object, [script]);
        var request = new QuestAcceptance(player, 2008, false, QuestAcceptorType.Npc, 9385);
        var calls = 0;
        bool Reject(QuestAcceptance accepted)
        {
            if (accepted.AcceptorType != QuestAcceptorType.Npc || accepted.AcceptorId != 9385)
                throw new InvalidOperationException("Script forwarded the wrong acceptor.");
            calls++;
            return false;
        }

        await Assert.That(manager.TryAcceptQuest(request, Reject)).IsFalse();
        await Assert.That(manager.TryAcceptQuest(request, Reject)).IsFalse();
        await Assert.That(calls).IsEqualTo(2);
        await Assert.That(npc.Encounter).IsNull();
        await Assert.That(spawner.Encounter).IsNull();
    }

    [Test]
    public async Task TryAcceptQuest_UnregisteredQuest_InvokesNormalAcceptanceOnce()
    {
        var manager = new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object);
        var request = new QuestAcceptance(new Character(null), 123, false, QuestAcceptorType.Item, 456);
        var calls = 0;

        var result = manager.TryAcceptQuest(request, accepted =>
        {
            if (accepted != request)
                throw new InvalidOperationException("Normal quest acceptance was changed.");
            calls++;
            return true;
        });

        await Assert.That(result).IsTrue();
        await Assert.That(calls).IsEqualTo(1);
    }

    [Test]
    public async Task TryAcceptQuest_RegisteredScript_CanRejectWithoutStartingQuest()
    {
        var script = new RejectingScript();
        var manager = new QuestManager(Mock.Of<ITaskManager>().Object, Mock.Of<IZoneManager>().Object, [script]);
        var calls = 0;

        var result = manager.TryAcceptQuest(new QuestAcceptance(new Character(null), 123, false,
            QuestAcceptorType.Npc, 456), _ => { calls++; return true; });

        await Assert.That(result).IsFalse();
        await Assert.That(script.AcceptCalls).IsEqualTo(1);
        await Assert.That(calls).IsEqualTo(0);
    }

    [Test]
    [Arguments(2008u, QuestComponentKind.Start, QuestStatus.Failed)]
    [Arguments(2008u, QuestComponentKind.Supply, QuestStatus.Failed)]
    [Arguments(2008u, QuestComponentKind.Progress, QuestStatus.Failed)]
    [Arguments(2008u, QuestComponentKind.Reward, QuestStatus.Progress)]
    [Arguments(2008u, QuestComponentKind.Ready, QuestStatus.Progress)]
    [Arguments(123u, QuestComponentKind.Progress, QuestStatus.Progress)]
    public async Task NotifyQuestLoaded_ReconnectWithoutNpc_FailsInterruptedAndPreservesCompletedQuests(uint id,
        QuestComponentKind step, QuestStatus expected)
    {
        var tasks = Mock.Of<ITaskManager>().Object;
        var script = new NymphQueenQuestScript(new Lazy<IQuestManager>(() => Mock.Of<IQuestManager>().Object),
            tasks, Mock.Of<ITeamManager>().Object, TimeProvider.System, new());
        var manager = new QuestManager(tasks, Mock.Of<IZoneManager>().Object, [script]);
        var quest = new Quest(null, new Character(null), manager, tasks, Mock.Of<ISkillManager>().Object,
            Mock.Of<IExpressTextManager>().Object, Mock.Of<IWorldManager>().Object)
            { TemplateId = id, Step = step, Status = QuestStatus.Progress };

        manager.NotifyQuestLoaded(quest);
        manager.NotifyQuestLoaded(quest);

        await Assert.That(quest.Status).IsEqualTo(expected);
    }

    [Test]
    public async Task NotifyQuestLoaded_RepeatedValidation_DoesNotAllocate()
    {
        var tasks = Mock.Of<ITaskManager>().Object;
        var script = new NymphQueenQuestScript(new Lazy<IQuestManager>(() => Mock.Of<IQuestManager>().Object),
            tasks, Mock.Of<ITeamManager>().Object, TimeProvider.System, new());
        var manager = new QuestManager(tasks, Mock.Of<IZoneManager>().Object, [script]);
        var quest = new Quest(null, new Character(null), manager, tasks, Mock.Of<ISkillManager>().Object,
            Mock.Of<IExpressTextManager>().Object, Mock.Of<IWorldManager>().Object)
            { TemplateId = 2008, Step = QuestComponentKind.Progress, Status = QuestStatus.Progress };
        quest.QuestSteps[QuestComponentKind.Progress] = new QuestStep(QuestComponentKind.Progress, quest);
        for (var i = 0; i < 100; i++)
            manager.NotifyQuestLoaded(quest);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            manager.NotifyQuestLoaded(quest);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        await Assert.That(allocated).IsEqualTo(0L);
        await Assert.That(quest.Status).IsEqualTo(QuestStatus.Failed);
    }

    private sealed class RejectingScript : IQuestScript
    {
        public uint QuestId => 123;
        public int AcceptCalls { get; private set; }
        public bool TryAccept(QuestAcceptance request, Func<QuestAcceptance, bool> accept)
        {
            AcceptCalls++;
            return false;
        }
        public void OnLoaded(Quest quest) { }
        public void OnRemoved(Quest quest, bool update) { }
    }
}
