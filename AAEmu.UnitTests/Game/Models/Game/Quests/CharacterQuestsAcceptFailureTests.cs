using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.World;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Drives the real <see cref="CharacterQuests.AddQuest(uint, bool, QuestAcceptorType, uint)"/> refusal
/// paths against a recording session. A refused accept that stops short of answering the client leaves
/// its Accept window hanging, and only the outgoing packet shows that.
/// </summary>
[NotInParallel]
public sealed class CharacterQuestsAcceptFailureTests
{
    private const uint QuestId = 4781;

    private readonly List<byte[]> _sentPackets = [];
    private readonly Character _character;

    public CharacterQuestsAcceptFailureTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        var connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams()) { Id = 42, Name = "Tester" };
        _character.Connection = connection;
        connection.ActiveChar = _character;
        _character.Quests = new CharacterQuests(_character);
    }

    [Before(Test)]
    public void Before() =>
        // A template-less manager is what an unloaded server has, so it is enough to reach the
        // refusals that depend on the template lookup.
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(new QuestManager(
                Mock.Of<ITaskManager>().Object,
                Mock.Of<IZoneManager>().Object))
            .BuildServiceProvider();

    [After(Test)]
    public void TearDown()
    {
        TodayQuestGameData.Instance.SetStepsForTest();
        SingletonContainer.ServiceProvider = null;
    }

    [Test]
    public async Task DuplicateAccept_TellsClientAlreadyHave()
    {
        _character.Quests.ActiveQuests[QuestId] = CreateQuest();

        var accepted = _character.Quests.AddQuest(QuestId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertRefusalPacket(QuestId, QuestStatusFailed.AlreadyHave);
    }

    [Test]
    public async Task GuildPublicAssignmentAccept_TellsClientTheQuestIsBlocked()
    {
        const uint questId = 10914;
        SeedGuildPublicAssignment(questId);

        var accepted = _character.Quests.AddQuest(questId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await Assert.That(_character.Quests.HasQuest(questId)).IsFalse();
        await AssertRefusalPacket(questId, QuestAcceptFailRules.PublicAssignmentBlocked);
    }

    [Test]
    public async Task UnknownQuestTemplate_TellsClientInvalidQuest()
    {
        var accepted = _character.Quests.AddQuest(QuestId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertRefusalPacket(QuestId, QuestStatusFailed.InvalidQuest);
    }

    [Test]
    public async Task SphereAcceptTheClientAskedFor_IsAnswered()
    {
        var accepted = _character.Quests.AddQuestFromSphere(QuestId, 7, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertRefusalPacket(QuestId, QuestStatusFailed.InvalidQuest);
    }

    [Test]
    public async Task MissingNpcSource_TellsClientInvalidNpcOrQuest()
    {
        PublishWorld();

        var accepted = _character.Quests.AddQuestFromNpc(QuestId, 0xDEAD, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertRefusalPacket(QuestId, QuestAcceptFailRules.MissingSource(QuestAcceptorType.Npc));
    }

    [Test]
    public async Task MissingDoodadSource_TellsClientInvalidDoodad()
    {
        PublishWorld();

        var accepted = _character.Quests.AddQuestFromDoodad(QuestId, 0xDEAD, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertRefusalPacket(QuestId, QuestAcceptFailRules.MissingSource(QuestAcceptorType.Doodad));
    }

    [Test]
    public async Task AutomaticAccepts_AreRefusedWithoutAnsweringTheClient()
    {
        // The server starts quests on its own: entering a quest-starter sphere, a chain's next quest
        // after a completion, a guild assignment probe. None of those has an Accept window open, so a
        // refusal there must not put an error on screen.
        PublishWorld();
        _character.Quests.ActiveQuests[QuestId] = CreateQuest();

        var duplicate = _character.Quests.AddQuest(QuestId);
        var sphere = _character.Quests.AddQuestFromSphere(QuestId + 1, 7);
        var chained = _character.Quests.AddQuest(QuestId + 2, false, QuestAcceptorType.Npc, 12);
        var missingNpc = _character.Quests.AddQuestFromNpc(QuestId + 3, 0xDEAD);

        await Assert.That(duplicate).IsFalse();
        await Assert.That(sphere).IsFalse();
        await Assert.That(chained).IsFalse();
        await Assert.That(missingNpc).IsFalse();
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    private void PublishWorld()
    {
        // Empty instance: every source lookup misses, which is the refusal under test. The public
        // ParentWorld setter re-resolves the instance through WorldManager.Instance, a process-wide
        // singleton that a refusal test has no business republishing, so cache it directly.
        var world = new WorldInstance(new WorldTemplate { Id = 3, Name = "unit_test" }, 0, true, 7);
        typeof(GameObject)
            .GetField("_parentWorld", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(_character, world);
    }

    private static void SeedGuildPublicAssignment(uint questId)
    {
        var step = new TodayQuestStepTemplate
        {
            Id = 41,
            RealStep = 13,
            SortId = TodayQuestStepTemplate.ExpeditionPublicBoardSortId
        };
        var group = new TodayQuestGroupTemplate { Id = 172, StepId = step.Id };
        group.QuestContextIds.Add(questId);
        step.Groups.Add(group);
        TodayQuestGameData.Instance.SetStepsForTest(step);
    }

    private Quest CreateQuest()
    {
        var template = Mock.Of<IQuestTemplate>();
        template.Components.Returns(new Dictionary<uint, QuestComponentTemplate>());

        return new Quest(
            template.Object,
            _character,
            Mock.Of<IQuestManager>().Object,
            Mock.Of<ITaskManager>().Object,
            Mock.Of<ISkillManager>().Object,
            Mock.Of<IExpressTextManager>().Object,
            Mock.Of<IWorldManager>().Object);
    }

    private async Task AssertRefusalPacket(uint questId, QuestStatusFailed reason)
    {
        // Exactly one answer, so a refusal can neither stay silent nor answer twice.
        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        var stream = new PacketStream(_sentPackets[0]);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)(stream.Count - 2));
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xdd);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        stream.ReadByte(); // transport checksum, unused for level 1
        stream.ReadByte(); // transport counter, unused for level 1
        await Assert.That(stream.ReadUInt16()).IsEqualTo(SCOffsets.SCQuestContextFailedPacket);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(questId);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)reason);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
