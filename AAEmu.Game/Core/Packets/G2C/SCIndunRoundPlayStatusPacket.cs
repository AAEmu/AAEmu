using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// TODO: nothing constructs this packet yet.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class SCIndunRoundPlayStatusPacket(bool playing, bool success, sbyte round, bool nextRoundBoss, bool showUi) : GamePacket(SCOffsets.SCIndunRoundPlayStatusPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(playing);
        stream.Write(success);
        stream.Write(round);
        stream.Write(nextRoundBoss);
        stream.Write(showUi);
        return stream;
    }
}
