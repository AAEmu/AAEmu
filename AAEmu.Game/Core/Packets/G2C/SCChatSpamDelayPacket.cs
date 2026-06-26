using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCChatSpamDelayPacket() : GamePacket(SCOffsets.SCChatSpamDelayPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        // 10.0.2.13 body:
        //   version(u8) reportDelay(u16) [chatTypeGroup[20](u8) chatGroupDelay[20](u32) whisperChatGroup(u8)]
        //   applyConfig(blob) detectConfig(blob)
        stream.Write((byte)0);    // version
        stream.Write((ushort)0);  // reportDelay

        for (var i = 0; i < 20; i++)
            stream.Write((byte)0); // chatTypeGroup[20]
        for (var i = 0; i < 20; i++)
            stream.Write((uint)0); // chatGroupDelay[20] (+240, 4 bytes each)
        stream.Write((byte)0);    // whisperChatGroup

        stream.Write(""); // applyConfig (blob, empty)
        stream.Write(""); // detectConfig (blob, empty)
        return stream;
    }
}
