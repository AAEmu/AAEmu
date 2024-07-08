using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCErrorMsgPacket : GamePacket
{
    private readonly short _errorMessage1;
    private readonly short _errorMessage2;
    private readonly uint _type;
    private readonly bool _isNotify;

    public SCErrorMsgPacket(ErrorMessageType errorMessage, uint type, bool isNotify) : base(SCOffsets.SCErrorMsgPacket, 1)
    {
        _errorMessage1 = (short)errorMessage; // TODO - NEED TO FIND ALL 350 IDS
        _errorMessage2 = _errorMessage1;
        _type = type;
        _isNotify = isNotify;
    }

    public SCErrorMsgPacket(ErrorMessageType errorMessage1, ErrorMessageType errorMessage2, uint type, bool isNotify) : base(SCOffsets.SCErrorMsgPacket, 1)
    {
        _errorMessage1 = (short)errorMessage1;
        _errorMessage2 = (short)errorMessage2;
        _type = type;
        _isNotify = isNotify;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_errorMessage1);
        stream.Write(_errorMessage2);
        stream.Write(_type);
        stream.Write(_isNotify);
        return stream;
    }
}
