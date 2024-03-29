using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSCompleteQuestContextPacket : GamePacket
{
    private uint _questContextId;
    private uint _npcObjId;
    private uint _doodadObjId;
    private int _selected;

    public CSCompleteQuestContextPacket() : base(CSOffsets.CSCompleteQuestContextPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        _questContextId = stream.ReadUInt32();
        _npcObjId = stream.ReadBc();
        _doodadObjId = stream.ReadBc();
        _selected = stream.ReadInt32();

        //Connection.ActiveChar.Quests.OnReportToNpc(_npcObjId, _questContextId, _selected);
        //Connection.ActiveChar.Quests.OnReportToDoodad(_doodadObjId, _questContextId, _selected);
        //owner.Quests.Complete(questContextId, selected, true);
        // инициируем событие
        //Task.Run(() => QuestManager.Instance.DoReportEvents(Connection.ActiveChar, _questContextId, _npcObjId, _doodadObjId, _selected));
        QuestManager.Instance.DoReportEvents(Connection.ActiveChar, _questContextId, _npcObjId, _doodadObjId, _selected);
    }

}
