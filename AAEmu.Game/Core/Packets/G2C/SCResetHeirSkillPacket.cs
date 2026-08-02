using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Confirms removal of an active Heir-skill successor.</summary>
/// <remarks>
/// u32 resetKind, i32 successorSkillId, i8 ability.
/// </remarks>
public class SCResetHeirSkillPacket(uint resetKind, int successorSkillId, sbyte ability)
    : GamePacket(SCOffsets.SCResetHeirSkillPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(resetKind);
        stream.Write(successorSkillId);
        stream.Write(ability);
        return stream;
    }
}
