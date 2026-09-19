using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Delta notification for the character sheet's game-point table. Display-only -
/// SCCharacterGamePointsPacket carries the values, this is what makes an already-open sheet repaint.
///
/// Wire format is a bare {u16 kind, u32 amount} - there is no entry count. Verified against the
/// 10.0.2.13 client: it reads the kind as a u16 and the amount as a u32, then prints "cached + amount",
/// so the delta has to be applied while the client's cache still holds the pre-change number.
///
/// Kind space: this client accepts only kind 1 (honor) on this opcode. Kinds 0 and 2..52 are silently
/// dropped - no chat line, no point change, no event - so every other point is announced by
/// SCCharacterGamePointsPacket plus a zero-amount honor delta whose only job is to raise that event.
/// </summary>
public class SCGamePointChangedPacket(byte kind, int amount) : GamePacket(SCOffsets.SCGamePointChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ushort)kind);
        stream.Write(amount);
        return stream;
    }
}
