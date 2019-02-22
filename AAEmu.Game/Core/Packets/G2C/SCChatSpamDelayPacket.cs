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
            stream.Write(0f); // yellDelay
            stream.Write(""); // applyConfig
            stream.Write(""); // detectConfig
            return stream;
        }

        /*
         * stream.Write(0f); // yellDelay
         * stream.Write(0); // maxSpamYell
         * stream.Write(0f); // spamYellDelay
         * stream.Write(0); // maxChatLen
         */
    }
}
