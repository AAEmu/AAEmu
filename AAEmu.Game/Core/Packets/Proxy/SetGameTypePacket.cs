using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.Proxy;

public class SetGameTypePacket : GamePacket
{
    private readonly string _level;
    private readonly ulong _checksum;
    private readonly byte _immersive;

    public SetGameTypePacket(string level, ulong checksum, byte immersive) : base(PPOffsets.SetGameTypePacket, 2)
    {
        _level = level;
        _checksum = checksum;
        _immersive = immersive;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_level);
        stream.Write(_checksum);
        stream.Write(_immersive);

        return stream;
    }
}
