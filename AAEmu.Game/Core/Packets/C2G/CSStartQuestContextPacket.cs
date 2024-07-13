using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSStartQuestContextPacket : GamePacket
{
    private uint _questContextId;
    private uint _npcObjId;
    private uint _doodadObjId;
    private uint _sphereId;
    public CSStartQuestContextPacket() : base(CSOffsets.CSStartQuestContextPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        _questContextId = stream.ReadUInt32(); // questContextId
        _npcObjId = stream.ReadBc();           // npcObjId
        _doodadObjId = stream.ReadBc();        // doodadObjId
        _sphereId = stream.ReadUInt32();       // selected

        if (_npcObjId > 0)
            Connection.ActiveChar.Quests.AddQuestFromNpc(_questContextId, _npcObjId);
        else if (_doodadObjId > 0)
            Connection.ActiveChar.Quests.AddQuestFromDoodad(_questContextId, _doodadObjId);
        else if (_sphereId > 0)
            Connection.ActiveChar.Quests.AddQuestFromSphere(_questContextId, _sphereId);
        else
            Connection.ActiveChar.Quests.AddQuest(_questContextId);
    }
}
