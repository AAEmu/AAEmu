using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// A suspected bot is going to a bot trial. Two separate 8-byte "type" fields, then the kicked flag.
/// </summary>
public class SCSuspectGoingBotTrialPacket(ulong @type, ulong @type2, bool kicked)
    : GamePacket(SCOffsets.SCSuspectGoingBotTrialPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(@type);
        stream.Write(@type2);
        stream.Write(kicked);
        return stream;
    }
}
