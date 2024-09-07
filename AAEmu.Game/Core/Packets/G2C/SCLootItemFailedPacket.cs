using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCLootItemFailedPacket : GamePacket
{
    private readonly int _errorMessage;
    private readonly ulong _iId;
    private readonly uint _id;
    private readonly uint _objid;

    public SCLootItemFailedPacket(ErrorMessageType errorMessage, ulong iId, uint id, uint objid) : base(SCOffsets.SCLootItemFailedPacket, 5)
    {
        _errorMessage = (int)errorMessage;
        _iId = iId;
        _id = id;
        _objid = objid;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_errorMessage);
        stream.Write(_iId);
        stream.Write(_id);
        stream.WriteBc(_objid);
        return stream;
    }
}
