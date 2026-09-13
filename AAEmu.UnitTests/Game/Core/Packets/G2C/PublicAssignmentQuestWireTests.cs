using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.TodayAssignment;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public sealed class PublicAssignmentQuestWireTests
{
    [Test]
    public async Task ProjectionWritesSharedObjectivesWithoutCharacterQuest()
    {
        var template = new QuestTemplate { Id = 10914 };
        template.Components[7] = new QuestComponentTemplate(template)
            { Id = 47479, KindId = QuestComponentKind.Progress };
        var state = new ExpeditionPublicAssignmentState
        {
            ExpeditionId = 41, PeriodStart = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc),
            RealStep = 13, GroupId = 196, QuestContextId = template.Id,
            Status = TodayAssignmentStatus.Progress
        };
        state.Objectives[0] = 299;

        var stream = new PublicAssignmentQuestWireState(template, state).Write(new PacketStream());
        stream.Rollback();

        await Assert.That(stream.ReadInt64()).IsEqualTo(-unchecked((long)(((ulong)41 << 32) | 13)));
        await Assert.That(stream.ReadUInt32()).IsEqualTo(10914u);
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)QuestStatus.Progress);
        var objectives = stream.ReadPisc(10);
        await Assert.That(objectives[0]).IsEqualTo(299u);
        await Assert.That(objectives.Skip(1).All(value => value == 0)).IsTrue();
    }
}
