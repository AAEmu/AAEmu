using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// TODO(v10): the body is parsed but nothing acts on it yet.
/// </summary>
/// <remarks>
/// packet has no body. Every parameterless C2S type folds onto that one function, so the
/// shared address is identical-COMDAT folding, not a base-class fall-through.
/// </remarks>
public class CSDepartToForeignServerPacket() : GamePacket(CSOffsets.CSDepartToForeignServerPacket, 1)
{
    public override void Read(PacketStream stream)
    {
    }
}
