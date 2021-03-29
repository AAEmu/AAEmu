using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCChatSpamConfigPacket : GamePacket
    {
        private readonly byte[] _applyConfig;
        private readonly byte[] _chatTypeGroup;
        private readonly float[] _chatGroupDelay;
        private readonly byte[] _detectConfig;
        public SCChatSpamConfigPacket() : base(SCOffsets.SCChatSpamConfigPacket, 5)
        {
            _applyConfig = new byte[]{ 0x00, 0x00 }; // 2
            _chatTypeGroup = new byte[]   { 0x01, 0x01, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }; // 18
            _chatGroupDelay = new float[] { 0f, 3f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f }; // 18
            _detectConfig = new byte[]    { 0x00, 0x00, 0x70, 0x42, 0x06, 0x00, 0x00, 0x00, 0x00, 0x00, 0x16, 0x44, 0xCD, 0xCC, 0x4C, 0x3F, 0x0A, 0xC8, 0x03 }; // 19
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte)2);                     // version = 0 or 2 ? in 4.0.3.6 = 2
            stream.Write((short)1);                   // reportDelay

            //for (var i = 0; i < 18; i++)               // in 1.2 = 15, in 1.7 = 15, in 1.8 = 15, in 2.0 ... 3.5 = 17, 18 in 7.5
            //    stream.Write((byte)0); // chatTypeGroup
            stream.Write(_chatTypeGroup, false);  // chatTypeGroup

            //for (var i = 0; i < 18; i++)               // in 1.2 = 15, in 1.7 = 15, in 1.8 = 15, in 2.0 ... 3.5 = 17, 18 in 7.5
            //    stream.Write(0f); // chatGroupDelay
            stream.Write(_chatGroupDelay, false);  // chatGroupDelay

            stream.Write((byte)0);                      // whisperChatGroup

            stream.Write(_applyConfig, true);  // applyConfig, len = 1
            stream.Write(_detectConfig, true); // detectConfig, len = 19

            return stream;
        }
    }
}
