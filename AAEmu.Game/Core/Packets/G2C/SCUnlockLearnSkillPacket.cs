using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Reports a live skill unlock result.</summary>
/// <remarks>
/// unlock set only when ErrorMessage is zero.
/// </remarks>
public class SCUnlockLearnSkillPacket(short errorMessage, int skillId)
    : GamePacket(SCOffsets.SCUnlockLearnSkillPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(errorMessage);
        stream.Write(skillId);
        return stream;
    }
}
