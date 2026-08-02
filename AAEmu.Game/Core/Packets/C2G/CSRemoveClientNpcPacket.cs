using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Tasks.Npcs;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// which writes <c>bc npcId</c>, then <c>i32 removeDelay</c>. ClientDrivenNpc constructor
/// (1000.0f), proving that removeDelay is milliseconds.
/// </remarks>
public class CSRemoveClientNpcPacket() : GamePacket(CSOffsets.CSRemoveClientNpcPacket, 1)
{
    public uint NpcId { get; private set; }
    public int RemoveDelay { get; private set; }

    public override void Read(PacketStream stream)
    {
        NpcId = stream.ReadBc();
        RemoveDelay = stream.ReadInt32();
    }

    public override void Execute()
    {
        var character = Connection.ActiveChar;
        if (character == null ||
            RemoveDelay < 0 ||
            character.ParentWorld.GetUnit(NpcId) is not Npc npc ||
            npc.OwnerId != character.Id)
        {
            Logger.Warn(
                "Rejected client NPC removal npc={0} delayMs={1} from {2} ({3})",
                NpcId, RemoveDelay, character?.Name ?? "<disconnected>", character?.ObjId ?? 0);
            return;
        }

        TaskManager.Instance.RemoveTasks(
            task => task is ClientNpcRemoveTask removeTask && removeTask.NpcObjId == NpcId);
        TaskManager.Instance.Schedule(
            new ClientNpcRemoveTask(npc, character.Id),
            TimeSpan.FromMilliseconds(RemoveDelay),
            count: 1);
    }
}
