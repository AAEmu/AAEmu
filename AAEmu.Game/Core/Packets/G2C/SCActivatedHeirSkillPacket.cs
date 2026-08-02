using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Confirms activation or replacement of a character's Heir skill.</summary>
/// <remarks>
/// i32 heirSkillId, i32 successorSkillId, bool isChange.
/// </remarks>
public class SCActivatedHeirSkillPacket(int heirSkillId, int successorSkillId, bool isChange) : GamePacket(SCOffsets.SCActivatedHeirSkillPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(heirSkillId);
        stream.Write(successorSkillId);
        stream.Write(isChange);
        return stream;
    }
}
