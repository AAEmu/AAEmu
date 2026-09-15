using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
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

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Drives the real <see cref="CharacterQuests.AddQuest(uint, bool, QuestAcceptorType, uint)"/> refusal
/// paths against a recording session. A refused accept that stops short of answering the client leaves
/// its Accept window hanging, and only the outgoing packet shows that.
/// </summary>
[NotInParallel]
public sealed class CharacterQuestsAcceptFailureTests
{
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

    [After(Test)]
    public void TearDown() => TodayQuestGameData.Instance.SetStepsForTest();

    [Test]
    public async Task DuplicateAccept_TellsClientAlreadyHave()
    {
        const uint questId = 4781;
        _character.Quests.ActiveQuests[questId] = CreateQuest();

        var accepted = _character.Quests.AddQuest(questId);

        await Assert.That(accepted).IsFalse();
        await AssertRefusalPacket(questId, QuestStatusFailed.AlreadyHave);
    }

    [Test]
    public async Task GuildPublicAssignmentAccept_TellsClientTheQuestIsBlocked()
    {
        const uint questId = 10914;
        SeedGuildPublicAssignment(questId);

        var accepted = _character.Quests.AddQuest(questId);

        await Assert.That(accepted).IsFalse();
        await Assert.That(_character.Quests.HasQuest(questId)).IsFalse();
        await AssertRefusalPacket(questId, QuestAcceptFailRules.PublicAssignmentBlocked);
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
