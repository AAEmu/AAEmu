using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCOneAndOneChatAddMessagePacket : GamePacket
{
    private readonly long _chat;
    private readonly string _speakerName;
    private readonly string _message;
    private readonly byte _isSpeakerGm;

    public SCOneAndOneChatAddMessagePacket(long chat, string speakerName, string message, bool isSpeakerGm) :
        base(SCOffsets.SCOneAndOneChatAddMessagePacket, 1)
    {
        _chat = chat;
        _speakerName = speakerName;
        _message = message;
        _isSpeakerGm = isSpeakerGm ? (byte)1 : (byte)0;
    }

    public override PacketStream Write(PacketStream stream)
    {
        // Field order taken from the 10.0.2.13 client's own serializer, which names each value:
        //   u64 chat, string speakerName, string message, u8 isSpeakerGm
        // The client raises ONE_AND_ONE_CHAT_ADD_MESSAGE(channelId, speakerName, message,
        // isSpeakerGm) from exactly these four values. It has no echo of its own: text typed into
        // the window is not displayed locally until this packet comes back, so the sender must be
        // sent this packet too, not only the peer.
        stream.Write((ulong)_chat);
        stream.Write(_speakerName ?? string.Empty);
        stream.Write(_message ?? string.Empty);
        stream.Write(_isSpeakerGm);
        return stream;
    }
}
