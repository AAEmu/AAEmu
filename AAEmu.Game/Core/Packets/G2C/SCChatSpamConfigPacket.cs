using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatSpamConfigPacket : GamePacket
    {
        private readonly byte[] _applyConfig;
        private readonly byte[] _detectConfig;
        public SCChatSpamConfigPacket() : base(SCOffsets.SCChatSpamConfigPacket, 5)
        {
            _applyConfig = new byte[] { 0x00 };
            _detectConfig = new byte[] { 0x00, 0x00, 0x70, 0x42, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x16, 0x44, 0xCD, 0xCC, 0x4C, 0x3F, 0x0A, 0xC8, 0x03 };
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)2); // version
            stream.Write((short)1); // reportDelay

            for (var i = 0; i < 17; i++)
            {
                stream.Write((byte)0); // chatTypeGroup
            }

            for (var i = 0; i < 17; i++)
            {
                stream.Write(0f); // chatGroupDelay
            }

            stream.Write((byte)0); // whisperChatGroup
            stream.Write(_applyConfig, true); // applyConfig
            stream.Write(_detectConfig, true); // detectConfig

            return stream;
        }
    }
}
