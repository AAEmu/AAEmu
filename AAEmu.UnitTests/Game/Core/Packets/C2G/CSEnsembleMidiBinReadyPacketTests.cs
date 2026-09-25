using System.Collections.Concurrent;
using System.Reflection;

using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Music;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

[NotInParallel]
public class CSEnsembleMidiBinReadyPacketTests
{
    private const uint Maestro = 0x601;
    private const uint Member = 0x602;

    [Test]
    public async Task UnsignedSizeAndRawBytes_AreReadWithoutTextDecoding()
    {
        var manager = new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object);
        var session = OpenSession();
        Ensembles(manager)[Maestro] = session;
        var world = EmptyWorldManager();
        var characters = (ConcurrentDictionary<uint, Character>)typeof(WorldManager)
            .GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world)!;
        var maestro = NewCharacter(Maestro, "Maestro");
        var member = NewCharacter(Member, "Member");
        characters[Maestro] = maestro;
        characters[Member] = member;

        var previousMusic = SwapSingleton(manager);
        var previousWorld = SwapSingleton(world);
        try
        {
            // Includes NUL, 0xFF and a non-UTF-8 byte: this is a MIDI block, not text.
            var raw = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00, 0x80 };
            var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = member };
            var packet = new CSEnsembleMidiBinReadyPacket { Connection = connection };
            packet.Read(Body((uint)raw.Length, raw));

            await Assert.That(packet.Size).IsEqualTo((uint)raw.Length);
            await Assert.That(packet.Data).IsEquivalentTo(raw);
            packet.Execute();
            await Assert.That(session.Parts.Contains(Member)).IsTrue();
        }
        finally
        {
            RestoreSingleton<WorldManager>(previousWorld);
            RestoreSingleton<MusicManager>(previousMusic);
        }
    }

    [Test]
    public async Task MalformedRawBody_IsRejectedBeforePartMutation()
    {
        var manager = new MusicManager(Mock.Of<IMusicIdManager>().Object, Mock.Of<IItemManager>().Object);
        var session = OpenSession();
        Ensembles(manager)[Maestro] = session;
        var world = EmptyWorldManager();
        var characters = (ConcurrentDictionary<uint, Character>)typeof(WorldManager)
            .GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(world)!;
        var maestro = NewCharacter(Maestro, "Maestro");
        var member = NewCharacter(Member, "Member");
        characters[Maestro] = maestro;
        characters[Member] = member;

        var previousMusic = SwapSingleton(manager);
        var previousWorld = SwapSingleton(world);
        try
        {
            var bodies = new[]
            {
                Body(0),
                // Declares 3 but the prefix promises 2 and only 2 bytes follow.
                Body(3, (ushort)2, [0x4D, 0x54]),
                Body((uint)EnsembleSession.MaximumPartBytes + 1),
                // Declares 2, prefix promises 2, but a third trailing byte is present.
                Body(2, (ushort)2, [0x4D, 0x54, 0x00]),
            };

            foreach (var body in bodies)
            {
                var connection = new GameConnection(Mock.Of<ISession>().Object) { ActiveChar = member };
                var packet = new CSEnsembleMidiBinReadyPacket { Connection = connection };
                packet.Read(body);

                await Assert.That(packet.Data).IsEmpty();
                packet.Execute();
                await Assert.That(session.Parts).IsEmpty();
            }
        }
        finally
        {
            RestoreSingleton<WorldManager>(previousWorld);
            RestoreSingleton<MusicManager>(previousMusic);
        }
    }

    [Test]
    public async Task OutboundWire_WritesUnsignedSizeThenTheLengthPrefixedBlob()
    {
        var raw = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00, 0x80 };
        var packet = new SCEnsembleMidiBinReadyPacket(Maestro, Member, (uint)raw.Length, raw);
        var body = new PacketStream();
        packet.Write(body);
        var bytes = body.GetBytes();

        // Two bc fields, the unsigned size, the u16 blob length, then the bytes. The prefix is what the
        // client reads the length from; without it the first two MIDI bytes are taken as the length.
        await Assert.That(bytes.Length).IsEqualTo(3 + 3 + sizeof(uint) + sizeof(ushort) + raw.Length);
        await Assert.That(bytes.Skip(0).Take(3)).IsEquivalentTo(new byte[] { 0x01, 0x06, 0x00 });
        await Assert.That(bytes.Skip(3).Take(3)).IsEquivalentTo(new byte[] { 0x02, 0x06, 0x00 });
        await Assert.That(BitConverter.ToUInt32(bytes, 6)).IsEqualTo((uint)raw.Length);
        await Assert.That(BitConverter.ToUInt16(bytes, 10)).IsEqualTo((ushort)raw.Length);
        await Assert.That(bytes.Skip(12)).IsEquivalentTo(raw);
    }

    /// <summary>
    /// The client and the relay must agree byte for byte. The server writes the size-prefixed blob the
    /// client reads; feeding that exact stream back through the client reader has to recover the payload.
    /// </summary>
    [Test]
    public async Task OutboundBytes_AreReadBackByTheClientReader()
    {
        var raw = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00, 0x80, 0x01, 0xF7 };
        var outbound = new SCEnsembleMidiBinReadyPacket(Maestro, Member, (uint)raw.Length, raw);
        var body = new PacketStream();
        outbound.Write(body);
        var bytes = body.GetBytes();

        var reader = new CSEnsembleMidiBinReadyPacket
        {
            Connection = new GameConnection(Mock.Of<ISession>().Object),
        };
        var inbound = new PacketStream();
        inbound.Write(bytes);
        reader.Read(inbound);

        await Assert.That(reader.Size).IsEqualTo((uint)raw.Length);
        await Assert.That(reader.Data).IsEquivalentTo(raw);
    }

    /// <summary>
    /// A block length that disagrees with the declared size leaves the payload boundary ambiguous, so the
    /// part is refused rather than guessed at. Each case pairs a mismatched prefix with a body whose byte
    /// count matches the PREFIX rather than the declared size, so only the agreement check can reject it —
    /// a reader that trusted the prefix and ignored trailing bytes would otherwise accept the first case.
    /// </summary>
    [Test]
    public async Task BlockLengthThatDisagreesWithTheDeclaredSize_IsRejected()
    {
        var sixBytes = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00 };
        var eightBytes = new byte[] { 0x4D, 0x54, 0x00, 0xFF, 0x2F, 0x00, 0x01, 0xF7 };

        var bodies = new[]
        {
            // Declares 8, prefix says 6, and exactly 6 bytes follow: the declared size is not honoured.
            Body(8, (ushort)6, sixBytes),
            // Declares 6, prefix says 8, and exactly 8 bytes follow: the block runs past the declaration.
            Body(6, (ushort)8, eightBytes),
            // A zero block length is not a valid empty part.
            Body(6, (ushort)0, sixBytes),
        };

        foreach (var body in bodies)
        {
            var reader = new CSEnsembleMidiBinReadyPacket
            {
                Connection = new GameConnection(Mock.Of<ISession>().Object),
            };
            reader.Read(body);
            await Assert.That(reader.Data).IsEmpty();
        }
    }

    private static CharacterMock NewCharacter(uint id, string name) => new()
    {
        Id = id,
        ObjId = id,
        Name = name,
        Buffs = null,
    };

    private static EnsembleSession OpenSession()
    {
        var session = new EnsembleSession(Maestro, "Maestro");
        session.Invite(Member);
        session.Accept(Member);
        return session;
    }

    /// <summary>
    /// Builds the body the client actually sends: two bc fields, the unsigned size, then the payload
    /// through the length-prefixed blob slot, which contributes a u16 block length ahead of the bytes.
    /// <paramref name="blockSize"/> overrides that prefix so a mismatched length can be exercised.
    /// </summary>
    private static PacketStream Body(uint size, params byte[] data) =>
        Body(size, (ushort)data.Length, data);

    private static PacketStream Body(uint size, ushort blockSize, byte[] data) =>
        new PacketStream()
            .WriteBc(Member)
            .WriteBc(Maestro)
            .Write(size)
            .Write((ushort)blockSize)
            .Write(data);

    private static Dictionary<uint, EnsembleSession> Ensembles(MusicManager manager) =>
        (Dictionary<uint, EnsembleSession>)typeof(MusicManager)
            .GetField("_ensembles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(manager)!;

    private static WorldManager EmptyWorldManager() => new(
        Mock.Of<ITickManager>().Object,
        Mock.Of<IWorldIdManager>().Object,
        new Lazy<IZoneManager>(() => Mock.Of<IZoneManager>().Object),
        new Lazy<IIndunManager>(() => Mock.Of<IIndunManager>().Object),
        new Lazy<IFamilyManager>(() => Mock.Of<IFamilyManager>().Object));

    private static object SwapSingleton<T>(T replacement) where T : class
    {
        var field = typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!;
        var previous = field.GetValue(null);
        field.SetValue(null, replacement);
        return previous;
    }

    private static void RestoreSingleton<T>(object previous) where T : class =>
        typeof(Singleton<T>).GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, previous);
}
