using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

public class ACChallenge2Packet : LoginPacket
{
    private readonly int _round;
    private readonly string _salt;
    private readonly uint[] _ch;

    public ACChallenge2Packet() : base(LCOffsets.ACChallenge2Packet)
    {
        _round = 5000;
        _salt = "xnDekI2enmWuAvwL"; //  length 16?
        _ch = new uint[8];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_round);    // round
        stream.Write(_salt);     // salt
        for (var i = 0; i < 8; i++)
        {
            stream.Write(_ch[i]); // ch
        }
        return stream;
    }
}
