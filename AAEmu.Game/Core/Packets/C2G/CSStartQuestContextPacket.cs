using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

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

        Logger.Info(
            "CSStartQuestContext quest={0} npcObj={1} doodadObj={2} sphere={3}",
            _questContextId,
            _npcObjId,
            _doodadObjId,
            _sphereId);

        // This is the only accept the character asked for, so it is the only one that gets an answer
        // when the accept is refused; the server-driven paths stay silent.
        if (_npcObjId > 0)
            Connection.ActiveChar.Quests.AddQuestFromNpc(_questContextId, _npcObjId, answerClient: true);
        else if (_doodadObjId > 0)
            Connection.ActiveChar.Quests.AddQuestFromDoodad(_questContextId, _doodadObjId, answerClient: true);
        else if (_sphereId > 0)
            Connection.ActiveChar.Quests.AddQuestFromSphere(_questContextId, _sphereId, answerClient: true);
        else
            Connection.ActiveChar.Quests.AddQuest(_questContextId, answerClient: true);
    }
}
