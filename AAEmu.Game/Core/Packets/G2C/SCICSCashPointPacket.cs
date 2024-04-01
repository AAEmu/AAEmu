using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCICSCashPointPacket : GamePacket
{
    private readonly int _point;
    private readonly int _bpoint;
    private readonly bool _relaod;

    public SCICSCashPointPacket(int point) : base(SCOffsets.SCICSCashPointPacket, 5)
    {
        _point = point;
        _bpoint = 0;     // bpoint;
        _relaod = false; //relaod;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_point); // point
        stream.Write(_point); // bpoint add in 3+
        stream.Write(_point); // relaod add in 3+
        return stream;
    }
}
