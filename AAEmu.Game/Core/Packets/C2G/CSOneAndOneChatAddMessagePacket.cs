using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSOneAndOneChatAddMessagePacket() : GamePacket(CSOffsets.CSOneAndOneChatAddMessagePacket, 1)
{
    /// <summary>
    /// The client serializer's own cap on this string field (u16 length prefix, policy 1023).
    /// Protocol constant, not gameplay content.
    /// </summary>
    public const int MaxMessageLength = 1023;

    /// <summary>Session id the server handed out in SCOneAndOneChatStartPacket.</summary>
    public long ChatId { get; private set; }

    public string Message { get; private set; }

    public override void Read(PacketStream stream)
    {
        // Field order taken from the 10.0.2.13 client's own serializer, which names each value:
        //   u64 chat, string message
        ChatId = (long)stream.ReadUInt64();
        Message = stream.ReadString();

        var character = Connection?.ActiveChar;
        if (character == null)
        {
            Logger.Error("CSOneAndOneChatAddMessage without an active character (chat={0})", ChatId);
            return;
        }

        var messageBytes = Message == null ? 0 : Encoding.UTF8.GetByteCount(Message);
        if (messageBytes == 0 || messageBytes > MaxMessageLength)
        {
            Logger.Error(
                "CSOneAndOneChatAddMessage from {0}: malformed message of {1} byte(s) for chat {2}",
                character.Name, messageBytes, ChatId);
            character.SendErrorMessage(ErrorMessageType.Invalid);
            return;
        }

        ChatManager.Instance.SendDirectChatMessage(character, ChatId, Message);
    }
}
