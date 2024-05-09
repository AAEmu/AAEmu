using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCChatSpamConfigPacket : GamePacket
{
    private readonly byte[] _applyConfig;
    private readonly byte[] _chatTypeGroup;
    private readonly float[] _chatGroupDelay;
    private readonly byte[] _detectConfig;
    public SCChatSpamConfigPacket() : base(SCOffsets.SCChatSpamConfigPacket, 5)
    {
        _chatTypeGroup = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00]; // 19 in 8+, 20 in 10810
        _chatGroupDelay = [0f, 2f, 2f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f]; // 19 in 8+, 20 in 10810
        _applyConfig = [0x00, 0x00]; // 2
        _detectConfig = [0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x70, 0x42, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]; // 19
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)1);             // version
        stream.Write((short)60);            // reportDelay

        //for (var i = 0; i < 20; i++) // 19 in 8+, 20 in 10810
        //    stream.Write((byte)0);           // chatTypeGroup
        stream.Write(_chatTypeGroup, false);  // chatTypeGroup

        //for (var i = 0; i < 18; i++)
        //    stream.Write(0f);                 // chatGroupDelay
        stream.Write(_chatGroupDelay, false);  // chatGroupDelay

        stream.Write((byte)0);             // whisperChatGroup

        stream.Write(_applyConfig, true);  // applyConfig
        stream.Write(_detectConfig, true); // detectConfig

        return stream;
    }
}
