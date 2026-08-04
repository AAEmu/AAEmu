using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.DoodadObj;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// Notifies the server that the client has observed a quest-relevant doodad.
/// </summary>
/// <remarks>
/// <c>+0x10</c> followed by the u32 doodad template ID at <c>+0x14</c>. ClientDoodad call sites
/// ClientDoodad template field. The client supplies no phase; the Zone-owned live doodad does.
/// </remarks>
public class CSDoodadQuestNotiPacket() : GamePacket(CSOffsets.CSDoodadQuestNotiPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var doodadObjId = stream.ReadBc();
        var doodadTemplateId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null || !character.Quests.ObserveQuestDoodad(doodadObjId, doodadTemplateId))
            return;

        var doodad = character.ParentWorld?.GetDoodad(doodadObjId);
        if (doodad == null || !doodad.IsVisible || doodad.TemplateId != doodadTemplateId)
            return;

        // same region-neighbourhood authority instead of inventing an interaction distance.
        if (!WorldManager.GetAround<Doodad>(character).Any(candidate => candidate.ObjId == doodadObjId))
            return;

        lock (doodad)
        {
            if (!doodad.IsVisible || doodad.TemplateId != doodadTemplateId)
                return;

            QuestManager.Instance.DoDoodadPhaseCheckEvents(
                character, doodad.TemplateId, doodad.FuncGroupId);
        }
    }
}
