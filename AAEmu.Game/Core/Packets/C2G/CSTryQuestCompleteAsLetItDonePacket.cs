using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSTryQuestCompleteAsLetItDonePacket() : GamePacket(CSOffsets.CSTryQuestCompleteAsLetItDonePacket, 1)
{
    private uint _id;
    private uint _objId;
    private int _selected;

    //

    public override void Read(PacketStream stream)
    {
        _id = stream.ReadUInt32();
        _objId = stream.ReadBc();
        _selected = stream.ReadInt32();

        Logger.Warn($"TryQuestCompleteAsLetItDone, Id: {_id}, ObjId: {_objId}, Selected: {_selected}");

        // Check if player is actually targeting the NPC
        if (
            _objId > 0
            && Connection.ActiveChar.CurrentTarget != null
            && Connection.ActiveChar.CurrentTarget.ObjId != _objId
           )
            return;
        Connection.ActiveChar.Quests.TryCompleteQuestAsLetItDone(_id, _selected);
    }
}

