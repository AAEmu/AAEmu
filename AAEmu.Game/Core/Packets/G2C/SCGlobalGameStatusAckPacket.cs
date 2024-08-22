using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCGlobalGameStatusAckPacket : GamePacket
{
    private readonly short _pktSize;
    private readonly byte[] _config;


    public SCGlobalGameStatusAckPacket() : base(SCOffsets.SCGlobalGameStatusAckPacket, 5)
    {
        _pktSize = 5;
        _config = [0x01, 0x00, 0x00, 0x00, 0x01];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_pktSize);
        stream.Write(_config, true);
        return stream;
    }
}
