using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCChatSpamConfigPacket : GamePacket
{
    private readonly byte[] _applyConfig;
    private readonly byte[] _detectConfig;
    private readonly byte[] _reportDelay;
    private readonly float[] _chatGroupDelay;
    public SCChatSpamConfigPacket() : base(SCOffsets.SCChatSpamConfigPacket, 5)
    {
        _applyConfig = [0x00];
        _detectConfig = [0x00, 0x00, 0x70, 0x42, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00, 0x34, 0x43, 0xCD, 0xCC, 0x4C, 0x3F, 0x0A, 0xFF, 0x01];
        _reportDelay = [0x00, 0x00, 0x02, 0x02, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x03, 0x00, 0x00];
        _chatGroupDelay = [0f, 1f, 3f, 6f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)2); // version
        stream.Write((short)60); // reportDelay

        //for (var i = 0; i < 17; i++)
        //{
        //    stream.Write((byte)0); // chatTypeGroup
        //}
        stream.Write(_reportDelay, false); // chatTypeGroup

        //for (var i = 0; i < 17; i++)
        //{
        //    stream.Write(0f); // chatGroupDelay
        //}
        foreach (var f in _chatGroupDelay)
        {
            stream.Write(f); // chatGroupDelay
        }

        stream.Write((byte)1); // whisperChatGroup
        stream.Write(_applyConfig, true); // applyConfig
        stream.Write(_detectConfig, true); // detectConfig

        return stream;
    }
}
