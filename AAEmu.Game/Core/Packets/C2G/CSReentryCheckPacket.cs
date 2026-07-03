using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

// C2S re-entry check (opcode 0x12E), sent fire-and-forget by the client during char-list, char-select and
// in-world. A live 10.0.2.13 capture shows the reference server sends no response, so this is a no-op that
// just consumes the packet — its only purpose is to keep the packet handler from logging it as unknown.
public class CSReentryCheckPacket() : GamePacket(CSOffsets.CSReentryCheckPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Body intentionally not parsed; the frame is length-delimited and the reference expects no reply.
    }
}
