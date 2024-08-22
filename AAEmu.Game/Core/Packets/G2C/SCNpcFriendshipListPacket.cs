using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCNpcFriendshipListPacket : GamePacket
{
    //private readonly List<Indun> _induns;
    private readonly int _count;

    public SCNpcFriendshipListPacket(/*List<Indun> _induns*/) : base(SCOffsets.SCNpcFriendshipListPacket, 5)
    {
        _count = 0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_count);
        //foreach (var indun in _induns)
        //    indun.Write(stream);

        return stream;
    }
}
