using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Updates a daily-schedule slot: stepId, groupId, questContextId, status, init.
/// stepId is today_quest_steps.id (not real_step).
/// </summary>
public class SCTodayAssignmentChangedPacket(int stepId, int groupId, int questContextId, sbyte status, bool init)
    : GamePacket(SCOffsets.SCTodayAssignmentChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(stepId);
        stream.Write(groupId);
        stream.Write(questContextId);
        stream.Write(status);
        stream.Write(init);
        return stream;
    }
}
