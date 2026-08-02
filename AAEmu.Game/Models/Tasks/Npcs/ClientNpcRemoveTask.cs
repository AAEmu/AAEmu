using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Tasks.Npcs;

/// <summary>Completes an owner-authorized CSRemoveClientNpc request after its native delay.</summary>
public class ClientNpcRemoveTask(Npc npc, uint ownerCharacterId) : Task
{
    public uint NpcObjId { get; } = npc?.ObjId ?? 0;

    public override void Execute()
    {
        if (npc?.ParentWorld?.GetUnit(NpcObjId) is not Npc liveNpc ||
            !ReferenceEquals(liveNpc, npc) ||
            liveNpc.OwnerId != ownerCharacterId)
        {
            return;
        }

        // Zone retires its authoritative object first and confirms with ZWRemoveNpc; standalone
        // Game deletes immediately through the same helper.
        WorldIntegration.DeleteNpcMirror(liveNpc, notifyZone: true);
    }
}
