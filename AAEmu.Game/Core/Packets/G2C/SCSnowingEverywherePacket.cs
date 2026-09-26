using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <remarks>
/// <para>
/// Carries the one-byte global snow toggle the client applies during world load.
/// </para>
/// </remarks>
public class SCSnowingEverywherePacket(bool on) : GamePacket(SCOffsets.SCSnowingEverywherePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(on);
        return stream;
    }
}
