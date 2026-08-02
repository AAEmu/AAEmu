using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Temporary skill marker on the world map.
/// </summary>
/// <remarks>
/// 39c64b80. Slot sequence 0x80,0x80,0x40,0x40,0x1a8,0x1a0: two i32 fields, then the direction probe, then
/// the write/read pair of one 3-byte compressed object id. The map overlay's own parameters — texture,
/// radius, view time — are read by the client from skill_map_effects for the skill it is told about, so the
/// wire only names the skill and whose marker it is.
/// </remarks>
public class SCSkillMapEffectPacket(int skillId, int effectId, uint objId)
    : GamePacket(SCOffsets.SCSkillMapEffectPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(skillId);
        stream.Write(effectId);
        stream.WriteBc(objId);
        return stream;
    }
}
