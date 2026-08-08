using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Client layout (VA 0x39C5B530): the challenger's id as u64, then duelType u8. We wrote a bare u32 -
/// five bytes short, so the challenge popup had no valid challenger to answer to.
/// </summary>
public class SCDuelChallengedPacket(uint challengerId, byte duelType = 0)
    : GamePacket(SCOffsets.SCDuelChallengedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)challengerId);  // u64 type - who is challenging
        stream.Write(duelType);             // u8  duelType

        return stream;
    }
}
