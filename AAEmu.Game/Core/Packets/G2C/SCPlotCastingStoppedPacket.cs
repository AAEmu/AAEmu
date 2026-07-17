using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCPlotCastingStoppedPacket(ushort tl, uint duration, byte lastEvent)
    : GamePacket(SCOffsets.SCPlotCastingStoppedPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        Logger.Warn("SCPlotCastingStoppedPacket: tl = {0} duration: {1} lastEvent: {2}", tl, duration, lastEvent);
        stream.Write(tl);
        stream.Write(duration);
        stream.Write(lastEvent);

        return stream;
    }
}
