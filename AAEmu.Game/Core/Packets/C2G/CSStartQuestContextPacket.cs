using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartQuestContextPacket() : GamePacket(CSOffsets.CSStartQuestContextPacket, 1)
{
    private uint _questContextId;
    private uint _npcObjId;
    private uint _doodadObjId;
    private uint _sphereId;

    public override void Read(PacketStream stream)
    {
        _questContextId = stream.ReadUInt32(); // questContextId
        _npcObjId = stream.ReadBc();           // npcObjId
        _doodadObjId = stream.ReadBc();        // doodadObjId
        _sphereId = stream.ReadUInt32();       // selected

        var character = Connection.ActiveChar;
        var quests = character.Quests;

        bool added;
        if (_npcObjId > 0)
        {
            var npc = character.ParentWorld.GetNpc(_npcObjId);
            // Quests 2396/2401 are data-driven as Doodad quests but the Doodad 14178 is missing;
            // allow NPC 7817 (Eokad Deltokin) to serve as the quest giver/turn-in proxy.
            if (npc != null && (_questContextId == 2396u || _questContextId == 2401u) && npc.TemplateId == 7817u)
            {
                added = quests.AddQuest(_questContextId, false, QuestAcceptorType.Doodad, 14178u);
            }
            else
            {
                added = quests.AddQuestFromNpc(_questContextId, _npcObjId);
            }
        }
        else if (_doodadObjId > 0)
        {
            added = quests.AddQuestFromDoodad(_questContextId, _doodadObjId);
        }
        else if (_sphereId > 0)
        {
            added = quests.AddQuestFromSphere(_questContextId, _sphereId);
        }
        else
        {
            added = quests.AddQuest(_questContextId);
        }

        if (added)
        {
            // Keep the client's quest list in sync after a new quest is accepted
            quests.Send();
        }
        else if (quests.ActiveQuests.TryGetValue(_questContextId, out var quest))
        {
            // Quest was already active; refresh the client so it switches to the progress UI
            character.SendPacket(new SCQuestContextUpdatedPacket(quest, quest.ComponentId));
            quests.Send();
        }
    }
}
