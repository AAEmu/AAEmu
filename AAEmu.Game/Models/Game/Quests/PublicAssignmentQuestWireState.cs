using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Expeditions.PublicAssignments;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.TodayAssignment;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>Client-only projection of shared guild progress; it is never inserted into Character.Quests.</summary>
public sealed class PublicAssignmentQuestWireState(QuestTemplate template, ExpeditionPublicAssignmentState state)
    : PacketMarshaler
{
    public uint ComponentId => template.GetFirstComponent(QuestComponentKind.Progress)?.Id ?? 0;

    public override PacketStream Write(PacketStream stream)
    {
        var instanceId = -checked((long)(((ulong)state.ExpeditionId << 32) | state.RealStep));
        stream.Write(instanceId);
        stream.Write(template.Id);
        stream.Write((byte)(state.Status == TodayAssignmentStatus.Done
            ? QuestStatus.Completed : QuestStatus.Progress));
        var objectives = new uint[10];
        for (var index = 0; index < objectives.Length; index++)
            objectives[index] = (uint)Math.Max(0, state.Objectives[index]);
        stream.WritePisc(objectives);
        stream.Write(false);
        stream.WriteBc(0u); stream.Write(0u); stream.WriteBc(0u); stream.WriteBc(0u);
        stream.Write(-1); stream.Write(0u); stream.Write(0L); stream.Write(state.PeriodStart);
        stream.Write((byte)QuestAcceptorType.Unknown); stream.Write(0u);
        return stream;
    }
}
