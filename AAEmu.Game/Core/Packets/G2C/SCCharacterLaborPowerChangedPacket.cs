using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// amount(i32) | localAmount(i32) | rechargedAmount(i32) | pisc(id, point) | step(u8).
/// Legacy layout wrote actability id/point as raw i32s and omitted the third amount —
/// client then hit "not enough buffer for pisc" and desynced the SC stream (frozen input).
/// </summary>
public class SCCharacterLaborPowerChangedPacket(
    int amount,
    int localAmount,
    int rechargedAmount,
    uint actabilityId,
    int actabilityPoint,
    byte step)
    : GamePacket(SCOffsets.SCCharacterLaborPowerChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(amount);
        stream.Write(localAmount);
        stream.Write(rechargedAmount);
        stream.WritePisc(actabilityId, (uint)Math.Max(0, actabilityPoint));
        stream.Write(step);
        return stream;
    }
}
