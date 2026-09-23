using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.StaticValues;
using AAEmu.Game.Models.Game.Units;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace AAEmu.UnitTests.Game.Core.Managers;

/// <summary>
/// GF-S17: the native one-to-one (one-and-one) chat message path - authorized delivery, blocked
/// delivery, offline handling, the content-configured rate limiter and loud failures.
/// </summary>
[NotInParallel]
public class DirectChatMessageTests
{
    private sealed class RecordingSession : ISession
    {
        public List<byte[]> Packets { get; } = [];
        public IPAddress Ip => IPAddress.Loopback;
        public uint SessionId => 1;
        public Socket Socket => null!;

        public void SendPacket(byte[] packet) => Packets.Add(packet);
        public void AddAttribute(string name, object attribute) { }
        public object GetAttribute(string name) => null;
        public void ClearAttribute(string name) { }
        public void Close() { }
    }

    private sealed record Fixture(Character Sender, Character Receiver, RecordingSession SenderWire,
        RecordingSession ReceiverWire);

    private static Fixture Pair(uint senderId, uint receiverId, uint senderMother, uint receiverMother)
    {
        var senderWire = new RecordingSession();
        var receiverWire = new RecordingSession();
        return new Fixture(
            OnlineCharacter(senderId, senderMother, senderWire),
            OnlineCharacter(receiverId, receiverMother, receiverWire),
            senderWire, receiverWire);
    }

    private static Character OnlineCharacter(uint id, uint motherFaction, ISession session)
    {
        var character = new Character(new UnitCustomModelParams())
        {
            Id = id,
            Name = $"Character {id}",
            Faction = Faction(motherFaction)
        };
        character.Connection = new GameConnection(session) { ActiveChar = character };
        typeof(Character).GetField("_isOnline", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, true);
        return character;
    }

    private static SystemFaction Faction(uint motherId) => new()
    {
        Id = (FactionsEnum)motherId, MotherId = (FactionsEnum)motherId, DiplomacyTarget = true
    };

    private static void SetOnline(Character character, bool online) =>
        typeof(Character).GetField("_isOnline", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(character, online);

    private static ushort TypeIdOf(byte[] bytes)
    {
        var stream = new PacketStream(bytes);
        SkipHeader(stream);
        return stream.ReadUInt16();
    }

    private static PacketStream BodyOf(byte[] bytes)
    {
        var stream = new PacketStream(bytes);
        SkipHeader(stream);
        stream.ReadUInt16(); // TypeId
        return stream;
    }

    private static void SkipHeader(PacketStream stream)
    {
        stream.ReadUInt16(); // total length
        stream.ReadByte();   // 0xDD flag
        stream.ReadByte();   // level
        stream.ReadByte();   // crc
        stream.ReadByte();   // counter
    }

    private static int CountOf(RecordingSession wire, ushort typeId) =>
        wire.Packets.Count(bytes => TypeIdOf(bytes) == typeId);

    private static (long chat, string speaker, string message, byte isGm) ReadAddMessage(byte[] bytes)
    {
        var body = BodyOf(bytes);
        return ((long)body.ReadUInt64(), body.ReadString(), body.ReadString(), body.ReadByte());
    }

    private static ErrorMessageType ReadErrorType(byte[] bytes)
    {
        var body = BodyOf(bytes);
        return (ErrorMessageType)body.ReadInt16();
    }

    [Test]
    public async Task NativeRequestOpcode_IsWiredToItsHandler()
    {
        // The packet class existing proves nothing on its own: the request has to be in the
        // level-1 table the network layer dispatches from.
        var network = GameNetwork.Instance;
        var handler = typeof(GameNetwork)
            .GetField("_handler", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(network);
        var table = (ConcurrentDictionary<byte, ConcurrentDictionary<uint, Type>>)
            typeof(GameProtocolHandler)
                .GetField("_packets", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(handler)!;

        await Assert.That(table[1][CSOffsets.CSOneAndOneChatAddMessagePacket])
            .IsEqualTo(typeof(CSOneAndOneChatAddMessagePacket));
    }

    [Test]
    public async Task AuthorizedSend_DeliversToTheReceiverAndEchoesBackToTheSender()
    {
        var manager = ChatManager.Instance;
        var pair = Pair(41001, 41002,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
        var session = manager.StartDirectChat(pair.Sender, pair.Receiver);
        await Assert.That(session).IsNotNull();
        pair.SenderWire.Packets.Clear();
        pair.ReceiverWire.Packets.Clear();

        var packet = new CSOneAndOneChatAddMessagePacket { Connection = pair.Sender.Connection };
        var request = new PacketStream().Write((ulong)session.Id).Write("hello there");
        packet.Read(request);

        await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
            .IsEqualTo(1);
        await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
            .IsEqualTo(1);

        var (chat, speaker, message, isGm) =
            ReadAddMessage(pair.ReceiverWire.Packets[^1]);
        await Assert.That(chat).IsEqualTo(session.Id);
        await Assert.That(speaker).IsEqualTo(pair.Sender.Name);
        await Assert.That(message).IsEqualTo("hello there");
        await Assert.That(isGm).IsEqualTo((byte)0);

        // The request consumed exactly the two fields the client writes.
        await Assert.That(request.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task SessionStart_IsAnnouncedToBothSidesOncePerPair()
    {
        var manager = new ChatManager();
        var pair = Pair(41011, 41012,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);

        var first = manager.StartDirectChat(pair.Sender, pair.Receiver);
        var second = manager.StartDirectChat(pair.Receiver, pair.Sender);
        await Assert.That(first).IsNotNull();
        await Assert.That(second).IsSameReferenceAs(first);

        await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCOneAndOneChatStartPacket)).IsEqualTo(1);
        await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatStartPacket)).IsEqualTo(1);

        // Each side is told who the window is with: targetName is the peer's name.
        var senderStart = BodyOf(pair.SenderWire.Packets[0]);
        await Assert.That((long)senderStart.ReadUInt64()).IsEqualTo(first.Id);
        await Assert.That(senderStart.ReadString()).IsEqualTo(pair.Receiver.Name);
        var receiverStart = BodyOf(pair.ReceiverWire.Packets[0]);
        await Assert.That((long)receiverStart.ReadUInt64()).IsEqualTo(first.Id);
        await Assert.That(receiverStart.ReadString()).IsEqualTo(pair.Sender.Name);
    }

    [Test]
    public async Task ABlockListEntry_RefusesTheWindowAndALaterMessage()
    {
        var manager = new ChatManager();
        var pair = Pair(41031, 41032,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
        pair.Receiver.Blocked = new CharacterBlocked(pair.Receiver);
        pair.Receiver.Blocked.BlockedList[pair.Sender.Id] = new BlockedTemplate
        {
            Owner = pair.Receiver.Id,
            BlockedId = pair.Sender.Id
        };

        await Assert.That(manager.StartDirectChat(pair.Sender, pair.Receiver)).IsNull();
        await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCOneAndOneChatStartPacket)).IsEqualTo(0);
        await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatStartPacket)).IsEqualTo(0);

        pair.Receiver.Blocked.BlockedList.Clear();
        var session = manager.StartDirectChat(pair.Sender, pair.Receiver);
        await Assert.That(session).IsNotNull();
        pair.Sender.Blocked = new CharacterBlocked(pair.Sender);
        pair.Sender.Blocked.BlockedList[pair.Receiver.Id] = new BlockedTemplate
        {
            Owner = pair.Sender.Id,
            BlockedId = pair.Receiver.Id
        };

        await Assert.That(manager.SendDirectChatMessage(pair.Sender, session.Id, "after the block")).IsEqualTo(0);
        await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket)).IsEqualTo(0);
    }

    [Test]
    public async Task ClosingASession_DropsItSoTheNextWhisperOpensANewOne()
    {
        var manager = new ChatManager();
        var pair = Pair(41041, 41042,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
        var first = manager.StartDirectChat(pair.Sender, pair.Receiver);
        await Assert.That(first).IsNotNull();

        await Assert.That(manager.CloseDirectChatSessions(pair.Sender)).IsEqualTo(1);
        await Assert.That(manager.SendDirectChatMessage(pair.Receiver, first.Id, "still there")).IsEqualTo(0);

        var again = manager.StartDirectChat(pair.Sender, pair.Receiver);
        await Assert.That(again).IsNotNull();
        await Assert.That(again.Id).IsNotEqualTo(first.Id);
        await Assert.That(manager.SendDirectChatMessage(pair.Sender, again.Id, "again")).IsEqualTo(2);
    }

    [Test]
    public async Task HostileFaction_IsBlockedAndNothingIsDelivered()
    {
        var manager = new ChatManager();

        // A hostile pair never gets a session, so there is no id their clients could even quote.
        var hostile = Pair(41021, 41022,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.HaranyaAlliance);
        await Assert.That(manager.StartDirectChat(hostile.Sender, hostile.Receiver)).IsNull();
        await Assert.That(CountOf(hostile.ReceiverWire, SCOffsets.SCOneAndOneChatStartPacket))
            .IsEqualTo(0);
        await Assert.That(SocialChatAuthorization.CanSendDirectChat(hostile.Sender, hostile.Receiver))
            .IsFalse();

        // An already-open session is not a bypass either: the rule is re-checked on every send.
        var pair = Pair(41023, 41024,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
        var session = manager.StartDirectChat(pair.Sender, pair.Receiver);
        await Assert.That(session).IsNotNull();
        pair.SenderWire.Packets.Clear();
        pair.ReceiverWire.Packets.Clear();
        pair.Sender.Faction = Faction((uint)FactionsEnum.HaranyaAlliance);

        var delivered = manager.SendDirectChatMessage(pair.Sender, session.Id, "spoofed");
        await Assert.That(delivered).IsEqualTo(0);
        await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
            .IsEqualTo(0);
        await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCErrorMsgPacket)).IsEqualTo(1);
        await Assert.That(ReadErrorType(pair.SenderWire.Packets[0]))
            .IsEqualTo(ErrorMessageType.ChatCannotWhisperToHostile);
    }

    [Test]
    public async Task OfflineReceiver_DropsTheMessageInsteadOfParkingIt()
    {
        var manager = new ChatManager();
        var pair = Pair(41031, 41032,
            (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
        var session = manager.StartDirectChat(pair.Sender, pair.Receiver);
        await Assert.That(session).IsNotNull();
        pair.SenderWire.Packets.Clear();
        pair.ReceiverWire.Packets.Clear();

        SetOnline(pair.Receiver, false);
        var delivered = manager.SendDirectChatMessage(pair.Sender, session.Id, "are you there?");

        await Assert.That(delivered).IsEqualTo(0);
        await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
            .IsEqualTo(0);
        // Nothing is queued for later delivery: the shipped content has no chat persistence and
        // the receiver is told the whisper target is gone.
        await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCErrorMsgPacket)).IsEqualTo(1);
        await Assert.That(ReadErrorType(pair.SenderWire.Packets[0]))
            .IsEqualTo(ErrorMessageType.WhisperNoTarget);
        await Assert.That(manager.SendDirectChatMessage(pair.Sender, session.Id, "still there?"))
            .IsEqualTo(0);
    }

    [Test]
    public async Task RateLimiter_UsesTheSeededContentRowAndDropsTheTooSoonSend()
    {
        ContentConfigGameData.Instance.SetForTest(ChatManager.DirectChatIntervalConfig, 60);
        try
        {
            var manager = new ChatManager();
            var pair = Pair(41041, 41042,
                (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
            var session = manager.StartDirectChat(pair.Sender, pair.Receiver);
            await Assert.That(session).IsNotNull();
            pair.SenderWire.Packets.Clear();
            pair.ReceiverWire.Packets.Clear();

            await Assert.That(manager.SendDirectChatMessage(pair.Sender, session.Id, "first"))
                .IsEqualTo(2);
            await Assert.That(manager.SendDirectChatMessage(pair.Sender, session.Id, "second"))
                .IsEqualTo(0);

            await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
                .IsEqualTo(1);

            // The interval is per sender: the other side of the same conversation still talks.
            await Assert.That(manager.SendDirectChatMessage(pair.Receiver, session.Id, "reply"))
                .IsEqualTo(2);
        }
        finally
        {
            ContentConfigGameData.Instance.RemoveForTest(ChatManager.DirectChatIntervalConfig);
        }
    }

    [Test]
    public async Task RateLimiter_MissingContentRow_DeliversAndLogsTheGapExactlyOnce()
    {
        ContentConfigGameData.Instance.RemoveForTest(ChatManager.DirectChatIntervalConfig);

        var previous = LogManager.Configuration;
        var memory = new MemoryTarget("directChatGap") { Layout = "${level}|${message}" };
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Warn, LogLevel.Fatal, memory);
        LogManager.Configuration = config;
        try
        {
            var manager = new ChatManager();
            var pair = Pair(41051, 41052,
                (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);
            var session = manager.StartDirectChat(pair.Sender, pair.Receiver);
            await Assert.That(session).IsNotNull();
            pair.SenderWire.Packets.Clear();
            pair.ReceiverWire.Packets.Clear();

            // No row, no fallback number: both sends go through...
            await Assert.That(manager.SendDirectChatMessage(pair.Sender, session.Id, "one"))
                .IsEqualTo(2);
            await Assert.That(manager.SendDirectChatMessage(pair.Sender, session.Id, "two"))
                .IsEqualTo(2);
            await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
                .IsEqualTo(2);

            // ...and the gap is reported loudly, once.
            LogManager.Flush();
            var gapLines = memory.Logs.Count(line =>
                line.Contains(ChatManager.DirectChatIntervalConfig, StringComparison.Ordinal));
            await Assert.That(gapLines).IsEqualTo(1);
        }
        finally
        {
            LogManager.Configuration = previous;
            ContentConfigGameData.Instance.RemoveForTest(ChatManager.DirectChatIntervalConfig);
        }
    }

    [Test]
    public async Task MalformedRequests_AreRejectedLoudlyAndDeliverNothing()
    {
        var previous = LogManager.Configuration;
        var memory = new MemoryTarget("directChatMalformed") { Layout = "${level}|${message}" };
        var config = new LoggingConfiguration();
        config.AddRule(LogLevel.Warn, LogLevel.Fatal, memory);
        LogManager.Configuration = config;
        try
        {
            var pair = Pair(41061, 41062,
                (uint)FactionsEnum.NuiaAlliance, (uint)FactionsEnum.NuiaAlliance);

            // Longer than the client serializer's own cap on the field.
            var tooLong = new string('x', CSOneAndOneChatAddMessagePacket.MaxMessageLength + 1);
            var overlong = new CSOneAndOneChatAddMessagePacket { Connection = pair.Sender.Connection };
            overlong.Read(new PacketStream().Write((ulong)1).Write(tooLong));
            await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCErrorMsgPacket)).IsEqualTo(1);
            await Assert.That(ReadErrorType(pair.SenderWire.Packets[0]))
                .IsEqualTo(ErrorMessageType.Invalid);

            // Empty text is not a message.
            var empty = new CSOneAndOneChatAddMessagePacket { Connection = pair.Sender.Connection };
            empty.Read(new PacketStream().Write((ulong)1).Write(string.Empty));
            await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCErrorMsgPacket)).IsEqualTo(2);

            // A well-formed message quoting a session that was never opened delivers nothing.
            var unknownSession = new CSOneAndOneChatAddMessagePacket { Connection = pair.Sender.Connection };
            unknownSession.Read(new PacketStream()
                .Write((ulong)long.MaxValue)
                .Write("where is everybody?"));
            await Assert.That(CountOf(pair.SenderWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
                .IsEqualTo(0);
            await Assert.That(CountOf(pair.ReceiverWire, SCOffsets.SCOneAndOneChatAddMessagePacket))
                .IsEqualTo(0);

            // Every one of them was reported.
            LogManager.Flush();
            var errors = memory.Logs.Count(line => line.StartsWith("Error|", StringComparison.Ordinal));
            await Assert.That(errors).IsGreaterThanOrEqualTo(3);
            await Assert.That(memory.Logs.Any(line =>
                line.Contains("unknown session", StringComparison.Ordinal))).IsTrue();
        }
        finally
        {
            LogManager.Configuration = previous;
        }
    }
}
