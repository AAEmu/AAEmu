using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCWorldRestrictOwnerChangePacket : GamePacket
{
    private readonly bool _worldRestrictOwnerChange;

    public SCWorldRestrictOwnerChangePacket(bool worldRestrictOwnerChange) : base(SCOffsets.SCWorldRestrictOwnerChangePacket, 5)
    {
        _worldRestrictOwnerChange = worldRestrictOwnerChange;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_worldRestrictOwnerChange);
        return stream;
    }
}
