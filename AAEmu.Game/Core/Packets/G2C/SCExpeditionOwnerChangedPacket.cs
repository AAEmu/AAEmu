using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// 2026-09-02: both id fields widened to 8 bytes - confirmed against the real client
/// (x2game.dll: PacketFunctor&lt;...,SCExpeditionOwnerChangedPacket&gt; -&gt; FUN_3933a7d0), which reads
/// two 8-byte values before the name string starts. The previous 4-byte writes would have under-filled
/// the body by 8 bytes total, misaligning the trailing name string.
/// </summary>
public class SCExpeditionOwnerChangedPacket(uint id, uint id2, string charName)
    : GamePacket(SCOffsets.SCExpeditionOwnerChangedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((ulong)id);
        stream.Write((ulong)id2);
        stream.Write(charName);
        return stream;
    }
}
