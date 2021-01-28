using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatSpamDelayPacket : GamePacket
    {
        private readonly byte[] _applyConfig;
        private readonly byte[] _detectConfig;
        public SCChatSpamDelayPacket() : base(SCOffsets.SCChatSpamDelayPacket, 5)
        {
            _applyConfig = new byte[] { 0x00 };
            _detectConfig = new byte[] { 0x00, 0x00, 0x70, 0x42, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x16, 0x44, 0xCD, 0xCC, 0x4C, 0x3F, 0x0A, 0xC8, 0x03 };
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)0);                      // version = 0 or 2 ? 
            stream.Write((short)1);                     // reportDelay

            for (var i = 0; i < 15; i++)               // in 1.2 = 15, in 1.7 = 15, in 3.5 = 17
            {
                stream.Write((byte)0); // chatTypeGroup
            }

            for (var i = 0; i < 15; i++)               // in 1.2 = 15, in 1.7 = 15, in 3.5 = 17
            {
                stream.Write(0f); // chatGroupDelay
            }

            stream.Write(_applyConfig, true);  // applyConfig, len = 1
            stream.Write(_detectConfig, true); // detectConfig, len = 19
            return stream;
        }
    }
}
