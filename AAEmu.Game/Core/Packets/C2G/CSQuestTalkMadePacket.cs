using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSQuestTalkMadePacket : GamePacket
{
    private uint _npcObjId;
    private uint _questContextId;
    private uint _questCompId;
    private uint _questActId;

    public CSQuestTalkMadePacket() : base(CSOffsets.CSQuestTalkMadePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        _npcObjId = stream.ReadBc();
        _questContextId = stream.ReadUInt32();
        _questCompId = stream.ReadUInt32();
        _questActId = stream.ReadUInt32();

        Logger.Warn("QuestTalkMade: npcObjId {0}, questContextId {1}, questCompId {2}, questActId {3}", _npcObjId, _questContextId, _questCompId, _questActId);
        //Connection.ActiveChar.Quests.OnTalkMade(_npcObjId, _questContextId, _questCompId, _questActId);
        // инициируем событие
        //Task.Run(() => QuestManager.Instance.DoTalkMadeEvents(Connection.ActiveChar, _npcObjId, _questContextId, _questCompId, _questActId));
        if (Connection != null)
        {
            QuestManager.Instance.DoTalkMadeEvents(Connection.ActiveChar, _npcObjId, _questContextId, _questCompId, _questActId);
        }
    }
}
