using System.IO;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public sealed class CSSendUserMusicPacketTests
{
    private const uint PlayerId = 31;
    private object _previousMusicManager;

    private static FieldInfo SingletonField<T>() where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;

    [Before(Test)]
    public void SetUp()
    {
        _previousMusicManager = SingletonField<MusicManager>().GetValue(null);
        SingletonField<MusicManager>().SetValue(null,
            new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object));
    }

    [After(Test)]
    public void TearDown() => SingletonField<MusicManager>().SetValue(null, _previousMusicManager);

    [Test]
    public async Task ReadMidiBlock_AcceptsCompleteDataWithOrWithoutTheOptionalNull()
    {
        var data = new byte[] { 0x4D, 0x54, 0x68, 0x64 };
        var withoutNull = CompleteBody(data.Length, checked((ushort)data.Length), data);
        var withNull = CompleteBody(data.Length, checked((ushort)data.Length), data, 0x00);

        var first = CSSendUserMusicPacket.ReadMidiBlock(withoutNull, out var firstSize);
        var second = CSSendUserMusicPacket.ReadMidiBlock(withNull, out var secondSize);

        await Assert.That(first).IsEquivalentTo(data);
        await Assert.That(second).IsEquivalentTo(data);
        await Assert.That(firstSize).IsEqualTo(data.Length);
        await Assert.That(secondSize).IsEqualTo(data.Length);
        await Assert.That(withoutNull.LeftBytes).IsEqualTo(0);
        await Assert.That(withNull.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task ReadMidiBlock_AllowsADataByteThatIsZeroAndOneTerminator()
    {
        var data = new byte[] { 0x4D, 0x54, 0x00 };
        var stream = CompleteBody(data.Length, checked((ushort)data.Length), data, 0x00);

        var parsed = CSSendUserMusicPacket.ReadMidiBlock(stream, out _);

        await Assert.That(parsed).IsEquivalentTo(data);
    }

    [Test]
    public async Task ReadMidiBlock_AcceptsTheProtocolBoundary()
    {
        var data = new byte[CSSendUserMusicPacket.MaximumMidiBytes];
        data[0] = 0x4D;
        data[^1] = 0x64;
        var stream = CompleteBody(data.Length, checked((ushort)data.Length), data);

        var parsed = CSSendUserMusicPacket.ReadMidiBlock(stream, out var songSize);

        await Assert.That(parsed.Length).IsEqualTo(CSSendUserMusicPacket.MaximumMidiBytes);
        await Assert.That(songSize).IsEqualTo(CSSendUserMusicPacket.MaximumMidiBytes);
    }

    [Test]
    public void ReadMidiBlock_RejectsTruncatedHeaders()
    {
        AssertInvalid(new PacketStream());
        AssertInvalid(new PacketStream(new PacketStream().Write(4).GetBytes()));
        AssertInvalid(new PacketStream(new PacketStream().Write(4).Write((byte)1).GetBytes()));
    }

    [Test]
    public void ReadMidiBlock_RejectsNonPositiveAndOutOfRangeDeclaredSizes()
    {
        AssertInvalid(CompleteBody(0, 0, []));
        AssertInvalid(CompleteBody(-1, 1, [0x4D]));
        AssertInvalid(CompleteBody(CSSendUserMusicPacket.MaximumMidiBytes + 1, 1, [0x4D]));
    }

    [Test]
    public void ReadMidiBlock_RejectsZeroOrMismatchedBlockSizes()
    {
        AssertInvalid(CompleteBody(1, 0, [0x4D]));
        AssertInvalid(CompleteBody(2, 1, [0x4D]));
        AssertInvalid(CompleteBody(1, 2, [0x4D, 0x54]));
    }

    [Test]
    public void ReadMidiBlock_RejectsTruncatedData()
    {
        AssertInvalid(CompleteBody(4, 4, [0x4D, 0x54, 0x68]));
    }

    [Test]
    public void ReadMidiBlock_RejectsUnexpectedTrailingData()
    {
        AssertInvalid(CompleteBody(1, 1, [0x4D], 0x01));
        AssertInvalid(CompleteBody(1, 1, [0x4D], 0x00, 0x00));
    }

    [Test]
    public async Task Read_ValidTrailingNullCachesOnlyTheMidiBytes()
    {
        var data = new byte[] { 0x4D, 0x54, 0x68, 0x64 };
        var packet = ConnectedPacket();

        packet.Read(CompleteBody(data.Length, checked((ushort)data.Length), data, 0x00));

        await Assert.That(MusicManager.Instance.TryGetMidiCache(PlayerId, out var cached)).IsTrue();
        await Assert.That(cached).IsEquivalentTo(data);
    }

    [Test]
    public async Task Read_FailedReplacementClearsThePreviousMidiBlock()
    {
        await Assert.That(MusicManager.Instance.CacheMidi(PlayerId, [0x4D, 0x54])).IsTrue();
        var packet = ConnectedPacket();
        var truncated = CompleteBody(4, 4, [0x4D, 0x54, 0x68]);

        Assert.Throws<InvalidDataException>(() => packet.Read(truncated));

        await Assert.That(MusicManager.Instance.TryGetMidiCache(PlayerId, out var cached)).IsFalse();
        await Assert.That(cached).IsNull();
    }

    private static TestSendPacket ConnectedPacket()
    {
        var character = new CharacterMock { Id = PlayerId };
        var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = character };
        var packet = new TestSendPacket();
        packet.Bind(connection);
        return packet;
    }

    private sealed class TestSendPacket : CSSendUserMusicPacket
    {
        public void Bind(GameConnection connection) => Connection = connection;
    }

    private static PacketStream CompleteBody(int declaredSize, ushort blockSize, byte[] data,
        params byte[] trailing)
    {
        var stream = new PacketStream()
            .Write(declaredSize)
            .Write(blockSize)
            .Write(data)
            .Write(trailing);
        return new PacketStream(stream.GetBytes());
    }

    private static void AssertInvalid(PacketStream stream) =>
        Assert.Throws<InvalidDataException>(() => CSSendUserMusicPacket.ReadMidiBlock(stream, out _));
}
