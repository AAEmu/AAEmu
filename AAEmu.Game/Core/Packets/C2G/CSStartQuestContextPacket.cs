using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

#pragma warning disable IDE0052 // Remove unread private members

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

        Connection.ActiveChar.Quests.Add(_questContextId, false, _npcObjId, _doodadObjId, _sphereId);
    }
}
