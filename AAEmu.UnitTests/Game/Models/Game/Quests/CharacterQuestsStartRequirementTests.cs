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
using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.TodayAssignment;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Game.Units.Static;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

/// <summary>
/// Drives the real <see cref="CharacterQuests.AddQuest"/> against a template whose Start component
/// carries unit_reqs rows, with a recording session, so the answer a refused row earns is pinned on
/// the wire: SCQuestUnitReqFailed with that row's result, and nothing else.
/// </summary>
[NotInParallel]
public sealed class CharacterQuestsStartRequirementTests
{
    // quest_contexts 9432 and its Start component 41095 (the first faction change quest); the rows
    // are synthetic so the outcome is under the test's control.
    private const uint QuestId = 9432;
    private const uint ComponentId = 41095;
    private const uint ObjId = 0x1A2B3C;

    private readonly List<byte[]> _sentPackets = [];
    private readonly Character _character;

    public CharacterQuestsStartRequirementTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        var connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams()) { Id = 42, Name = "Tester", ObjId = ObjId, Level = 10 };
        _character.Connection = connection;
        connection.ActiveChar = _character;
        _character.Quests = new CharacterQuests(_character);
    }

    [Before(Test)]
    public void Before() =>
        SingletonContainer.ServiceProvider = new ServiceCollection()
            .AddSingleton(new QuestManager(
                Mock.Of<ITaskManager>().Object,
                Mock.Of<IZoneManager>().Object))
            .BuildServiceProvider();

    [After(Test)]
    public void TearDown()
    {
        Templates().Remove(QuestId);
        UnitRequirementsGameData.Instance.SetForTest();
        TodayQuestGameData.Instance.SetStepsForTest();
        SingletonContainer.ServiceProvider = null;
    }

    [Test]
    public async Task RefusedRow_IsAnsweredWithTheRowsResult()
    {
        Seed(orUnitReqs: false, Row(1, UnitReqsKindType.Level, 60));

        var accepted = _character.Quests.AddQuest(QuestId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await Assert.That(_character.Quests.HasQuest(QuestId)).IsFalse();
        var body = await SingleUnitReqFailedBody();
        await Assert.That(body.ReadUInt32()).IsEqualTo(QuestId);
        await Assert.That(body.ReadBc()).IsEqualTo(ObjId);
        await Assert.That(body.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(body.ReadByte()).IsEqualTo((byte)SkillResult.UrkLevel);
        await Assert.That(body.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task RowWithoutDisplayMessage_CarriesTheGateOff()
    {
        Seed(orUnitReqs: false, Row(1, UnitReqsKindType.Level, 60, display: false));

        _character.Quests.AddQuest(QuestId, answerClient: true);

        var body = await SingleUnitReqFailedBody();
        body.ReadUInt32();
        body.ReadBc();
        await Assert.That(body.ReadByte()).IsEqualTo((byte)0x09);
        await Assert.That(body.ReadByte()).IsEqualTo((byte)SkillResult.UrkLevel);
        await Assert.That(body.ReadByte()).IsEqualTo((byte)0x00);
        await Assert.That(body.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task AndGroup_ReportsTheRowThatRefusedFirst()
    {
        Seed(orUnitReqs: false, Row(1, UnitReqsKindType.Gender, 2), Row(2, UnitReqsKindType.Level, 60));

        _character.Quests.AddQuest(QuestId, answerClient: true);

        var body = await SingleUnitReqFailedBody();
        body.ReadUInt32();
        body.ReadBc();
        body.ReadByte();
        await Assert.That(body.ReadByte()).IsEqualTo((byte)SkillResult.UrkGender);
    }

    [Test]
    public async Task OrGroup_Exhausted_AnswersUnitReqsOrFail()
    {
        Seed(orUnitReqs: true, Row(1, UnitReqsKindType.Gender, 2, display: false), Row(2, UnitReqsKindType.Level, 60));

        var accepted = _character.Quests.AddQuest(QuestId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        var body = await SingleUnitReqFailedBody();
        body.ReadUInt32();
        body.ReadBc();
        await Assert.That(body.ReadByte()).IsEqualTo((byte)0x01);
        await Assert.That(body.ReadByte()).IsEqualTo((byte)SkillResult.UnitReqsOrFail);
        await Assert.That(body.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task OrGroup_PassingRow_LetsTheAcceptReachTheNextGate()
    {
        // Level 10 satisfies the second row, so the requirement gate passes and the accept runs on to
        // the completed-quest gate, whose refusal still goes through SCQuestContextFailed.
        Seed(orUnitReqs: true, Row(1, UnitReqsKindType.Level, 60), Row(2, UnitReqsKindType.Level, 5));
        _character.Quests.SetCompletedQuestFlag(QuestId, true);

        var accepted = _character.Quests.AddQuest(QuestId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertContextFailedPacket(QuestStatusFailed.AlreadyCompleted);
    }

    [Test]
    public async Task LevelGate_StillAnswersWithSCQuestContextFailed()
    {
        Seed(orUnitReqs: false, Row(1, UnitReqsKindType.Level, 60));
        Templates()[QuestId].MinLevel = 20;

        var accepted = _character.Quests.AddQuest(QuestId, answerClient: true);

        await Assert.That(accepted).IsFalse();
        await AssertContextFailedPacket(QuestAcceptFailRules.LevelNotMet);
    }

    [Test]
    public async Task ServerDrivenAccept_StaysSilent()
    {
        Seed(orUnitReqs: false, Row(1, UnitReqsKindType.Level, 60));

        var accepted = _character.Quests.AddQuest(QuestId);

        await Assert.That(accepted).IsFalse();
        await Assert.That(_sentPackets.Count).IsEqualTo(0);
    }

    private static Dictionary<uint, QuestTemplate> Templates() =>
        (Dictionary<uint, QuestTemplate>)typeof(QuestManager)
            .GetField("_questTemplates", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(QuestManager.Instance)!;

    private static void Seed(bool orUnitReqs, params UnitReqs[] rows)
    {
        var template = new QuestTemplate { Id = QuestId };
        template.Components[ComponentId] = new QuestComponentTemplate(template)
        {
            Id = ComponentId,
            KindId = QuestComponentKind.Start,
            OrUnitReqs = orUnitReqs
        };
        Templates()[QuestId] = template;
        UnitRequirementsGameData.Instance.SetForTest(rows);
    }

    private static UnitReqs Row(uint id, UnitReqsKindType kind, uint value1, bool display = true) =>
        new()
        {
            Id = id,
            OwnerId = ComponentId,
            OwnerType = "QuestComponent",
            KindType = kind,
            Value1 = value1,
            DisplayMessage = display
        };

    private async Task<PacketStream> SingleUnitReqFailedBody()
    {
        var stream = await SinglePacket();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(SCOffsets.SCQuestUnitReqFailedPacket);
        return stream;
    }

    private async Task AssertContextFailedPacket(QuestStatusFailed reason)
    {
        var stream = await SinglePacket();
        await Assert.That(stream.ReadUInt16()).IsEqualTo(SCOffsets.SCQuestContextFailedPacket);
        await Assert.That(stream.ReadUInt32()).IsEqualTo(QuestId);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)reason);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    private async Task<PacketStream> SinglePacket()
    {
        // Exactly one answer, so a refusal can neither stay silent nor answer twice.
        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        var stream = new PacketStream(_sentPackets[0]);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)(stream.Count - 2));
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)0xdd);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        stream.ReadByte(); // transport checksum, unused for level 1
        stream.ReadByte(); // transport counter, unused for level 1
        return stream;
    }
}
