using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRaceCongestionPacket : GamePacket
{
    private readonly bool _forbidCharCreating;

    public SCRaceCongestionPacket() : base(SCOffsets.SCRaceCongestionPacket, 5)
    {
        _forbidCharCreating = false;
    }

    public override PacketStream Write(PacketStream stream)
    {
        for (var i = 0; i < 10; i++) // 9 in 3+, 10 in 10810
            stream.Write((byte)0);
        /*RACE_CONGESTION = {
            LOW = 0,
            MIDDLE = 1,
            HIGH = 2,
            FULL = 3,
            PRE_SELECT_RACE_FULL = 9,
            CHECK = 10
        }*/
        stream.Write(_forbidCharCreating); // add in 3.5.0.3
        return stream;
    }
}
