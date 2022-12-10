using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatSpamDelayPacket : GamePacket
    {
        public SCChatSpamDelayPacket() : base(SCOffsets.SCChatSpamDelayPacket, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)0); // version

            for (var i = 0; i < 15; i++)
                stream.Write((byte)0); // chatTypeGroup
            
            for (var i = 0; i < 15; i++)
                stream.Write(0f); // chatGroupDelay
            
            stream.Write(""); // applyConfig
            stream.Write(""); // detectConfig
            return stream;
        }
    }
}
