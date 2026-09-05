using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Answers CSExpeditionBuffUnitPacket, sent repeatedly by the client while the prestige-shop buff
/// window is open. Wire: unitId (Bc), then the same 12-byte {buffType, grade, learnedGrade} entries
/// as SCExpeditionBuffsPacket.
/// TODO: semantics of "why per-unit" (vs the guild-wide SCExpeditionBuffsPacket) unconfirmed; modeled
/// as an echo of the requesting character's own unit plus the guild's buff-grade state. Untested live.
/// </summary>
public class SCExpeditionBuffUnitPacket(uint unitId, IReadOnlyDictionary<uint, byte> purchasedGrades)
    : GamePacket(SCOffsets.SCExpeditionBuffUnitPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(purchasedGrades.Count);
        foreach (var (buffId, grade) in purchasedGrades)
        {
            stream.Write(buffId);
            stream.Write((uint)grade);
            stream.Write((uint)grade);
        }
        return stream;
    }
}
