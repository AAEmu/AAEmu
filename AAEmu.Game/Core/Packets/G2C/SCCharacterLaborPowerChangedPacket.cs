using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCCharacterLaborPowerChangedPacket : GamePacket
{
    private readonly int _amount;
    private readonly int _action;
    private readonly int _point;
    private readonly byte _step;

    public SCCharacterLaborPowerChangedPacket(int amount, int action, int point, byte step)
        : base(SCOffsets.SCCharacterLaborPowerChangedPacket, 5)
    {
        _amount = amount;
        _action = action;
        _point = point;
        _step = step;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_amount);
        stream.Write(0); // localAmount
        stream.Write(0); // rechargedAmount
        stream.WritePisc(_action, _point);
        stream.Write(_step);
        return stream;
    }
}
