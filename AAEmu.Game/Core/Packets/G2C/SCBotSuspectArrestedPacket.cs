using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A suspected bot was arrested by a sheriff. Both leading fields are 8-byte values the client reads
/// as "type" - they are two separate fields, not one value written twice.
/// </summary>
public class SCBotSuspectArrestedPacket(ulong @type, ulong @type2, string sheriffName, string suspectName)
    : GamePacket(SCOffsets.SCBotSuspectArrestedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(sheriffName);
        stream.Write(suspectName);
        return stream;
    }
}
