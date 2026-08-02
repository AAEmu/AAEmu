using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>Updates one skill-active-type mapping.</summary>
/// <remarks>
/// </remarks>
public class SCUpdateSkillActiveTypePacket(SkillActiveTypeEntry entry)
    : GamePacket(SCOffsets.SCUpdateSkillActiveTypePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(entry.HeirSkillType);
        stream.Write(entry.SkillType);
        stream.Write(entry.ActiveType);
        return stream;
    }
}
