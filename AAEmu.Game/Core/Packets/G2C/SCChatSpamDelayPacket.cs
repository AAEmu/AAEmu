using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatSpamDelayPacket : GamePacket
    {
        public SCChatSpamDelayPacket() : base(0x0cb, 1)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(0f); // yellDelay
            stream.Write(0); // maxSpamYell
            stream.Write(0f); // spamYellDelay
            stream.Write(0); // maxChatLen
            return stream;
        }
    }
}