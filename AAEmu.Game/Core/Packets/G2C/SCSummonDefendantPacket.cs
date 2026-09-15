using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells a client it is the defendant of a trial. The client takes its role from this packet - it is
/// what arms the defendant's wait window, his early-guilty plea and his final-statement dialog - so it
/// has to go out before the phases that use them.
/// </summary>
/// <remarks>Body: the trial id (u64).</remarks>
public class SCSummonDefendantPacket(ulong trial) : GamePacket(SCOffsets.SCSummonDefendantPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(trial);
        return stream;
    }
}
