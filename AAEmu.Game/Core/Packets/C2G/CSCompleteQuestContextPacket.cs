using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCompleteQuestContextPacket() : GamePacket(CSOffsets.CSCompleteQuestContextPacket, 1)
{
    private uint _questContextId;
    private uint _npcObjId;
    private uint _doodadObjId;
    private int _selected;

    public override void Read(PacketStream stream)
    {
        _questContextId = stream.ReadUInt32();
        _npcObjId = stream.ReadBc();
        _doodadObjId = stream.ReadBc();
        _selected = stream.ReadInt32();

        var character = Connection.ActiveChar;

        // Trigger report events so NPC/doodad acts can set Ready/override flags.
        QuestManager.Instance.DoReportEvents(character, _questContextId, _npcObjId, _doodadObjId, _selected);

        if (character.Quests.ActiveQuests.TryGetValue(_questContextId, out var quest))
        {
            // Once Ready, manually drive the final reward/complete steps.
            if (quest.Step == QuestComponentKind.Ready)
            {
                quest.RunCurrentStep(); // Ready -> Reward
                quest.RunCurrentStep(); // Reward -> Complete
            }
            else if (quest.Step == QuestComponentKind.Reward)
            {
                quest.RunCurrentStep(); // Reward -> Complete
            }
        }
    }

}
