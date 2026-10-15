using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Installs the guild's purchased prestige-shop buff grades on one unit so the client recomputes that
/// unit's attributes (Max Hp/Mp, etc.) from local <c>unit_modifiers</c>. Wire: unitId (Bc), then the
/// same Size + 12-byte {type, grade, learnedGrade} list as <see cref="SCExpeditionBuffsPacket"/>.
/// Pushed after purchase/login/leave; also answers <c>CSExpeditionBuffUnit</c> while the shop is open.
/// Guild-wide <see cref="SCExpeditionBuffsPacket"/> only refreshes the shop UI and is not enough.
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
