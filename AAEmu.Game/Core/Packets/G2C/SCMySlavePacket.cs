using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// bc(unitId), tl s16, slaveName[1024], type s32, hp u64, maxHp u64, pos(s64 x, s64 y, f32 z).
/// hp/maxHp are 64-bit here — the packet class holds them at +0x420 and +0x428.
/// </summary>
public class SCMySlavePacket(
    uint unitId,
    ushort tl,
    string slaveName,
    uint templateId,
    int hp,
    int maxHp,
    float x,
    float y,
    float z)
    : GamePacket(SCOffsets.SCMySlavePacket, 1)
{
    // TODO slaveId

    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unitId);
        stream.Write(tl);
        stream.Write(slaveName);
        stream.Write(templateId);
        stream.Write((ulong)hp);
        stream.Write((ulong)maxHp);
        stream.Write(Helpers.ConvertLongX(x));
        stream.Write(Helpers.ConvertLongY(y));
        stream.Write(z);
        return stream;
    }
}
