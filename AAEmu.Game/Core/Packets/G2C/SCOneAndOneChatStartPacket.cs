using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCOneAndOneChatStartPacket : GamePacket
{
    private readonly long _chat;
    private readonly string _targetName;

    public SCOneAndOneChatStartPacket(long chat, string targetName) :
        base(SCOffsets.SCOneAndOneChatStartPacket, 1)
    {
        _chat = chat;
        _targetName = targetName;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Field order taken from the 10.0.2.13 client's own serializer, which names each value:
        //   u64 chat, string targetName
        // The client turns this into ONE_AND_ONE_CHAT_START(channelId, targetName), which is what
        // opens the per-contact one-to-one window; targetName is the name of the peer this
        // window is with, so each side gets the other participant's name.
        stream.Write((ulong)_chat);
        stream.Write(_targetName ?? string.Empty);
        return stream;
    }
}
