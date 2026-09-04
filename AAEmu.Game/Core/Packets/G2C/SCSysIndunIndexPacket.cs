using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Reply to <see cref="C2G.CSRequestSysInstanceIndexPacket"/> — unlocks H-window Enter.
/// </summary>
/// <remarks>Wire: u32 zoneId (zone key), u32 instanceId (world copy id or 0), u32 instanceIndex (channel).</remarks>
public class SCSysIndunIndexPacket(uint zoneKey, uint instanceId, uint instanceIndex)
    : GamePacket(SCOffsets.SCSysIndunIndexPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(zoneKey);
        stream.Write(instanceId);
        stream.Write(instanceIndex);
        return stream;
    }
}
