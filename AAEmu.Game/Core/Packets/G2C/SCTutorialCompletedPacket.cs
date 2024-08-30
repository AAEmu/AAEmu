using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTutorialCompletedPacket : GamePacket
{
    private readonly uint _id;
    private readonly byte[] _body;

    public SCTutorialCompletedPacket(uint id, byte[] body) : base(SCOffsets.SCTutorialCompletedPacket, 5)
    {
        _id = id;
        _body = body;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_id);
        stream.Write(_body);
        return stream;
    }
}
