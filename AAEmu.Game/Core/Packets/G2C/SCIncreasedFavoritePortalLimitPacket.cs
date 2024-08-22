using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCIncreasedFavoritePortalLimitPacket : GamePacket
{
    private readonly uint _amount;

    public SCIncreasedFavoritePortalLimitPacket(uint amount) : base(SCOffsets.SCIncreasedFavoritePortalLimitPacket, 5)
    {
        _amount = amount;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_amount);
        return stream;
    }
}
