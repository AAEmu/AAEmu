using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Full sync of a guild's currently-purchased prestige-shop buff grades - answers CSExpeditionBuffPacket's
/// "view" request and should also be pushed on Expedition join/login (see ExpeditionManager.SendExpeditionBuffs).
/// </summary>
/// <remarks>
/// Wire layout: expeditionId (u32), freshness timestamp (i64), buff count (i32), then per buff a
/// 12-byte {buffType, grade, learnedGrade} triple. Each field must be sent in that exact order -
/// swapping grade/learnedGrade makes the client's buff-shop numerator read 0.
/// TODO: learnedGrade's exact semantics are unconfirmed; sending the same value as grade is a safe
/// default but may need to differ in some case.
/// </remarks>
public class SCExpeditionBuffsPacket(uint expeditionId, IReadOnlyDictionary<uint, byte> purchasedGrades)
    : GamePacket(SCOffsets.SCExpeditionBuffsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(expeditionId);
        stream.Write(DateTime.UtcNow.Ticks);
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
