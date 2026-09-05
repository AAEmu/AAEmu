using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSRequestUIDataPacketTests
{
    private readonly UiDataPacketTestStore _store = new();
    private readonly List<byte[]> _sentPackets = [];
    private readonly GameConnection _connection;
    private readonly Character _character;
    private readonly CSRequestUIDataPacket _packet;

    public CSRequestUIDataPacketTests()
    {
        var session = Mock.Of<ISession>();
        session.SendPacket(Any<byte[]>()).Callback((byte[] bytes) => _sentPackets.Add(bytes));
        _connection = new GameConnection(session.Object);
        _character = new Character(new UnitCustomModelParams(), _store) { Id = 42 };
        _character.SetOption(7, "stored-value");
        _connection.Characters.Add(_character.Id, _character);
        _packet = new CSRequestUIDataPacket { Connection = _connection };
    }

    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(20)]
    public async Task Read_UsesOwnedPacketCharacterWithNullActiveCharacter(int type)
    {
        var key = (ushort)type;
        const string data = " \t{ \"unknown_test_field\" : \"\U0001F642\" }\r\n ";
        _character.SetOption(key, data);
        var stream = CreateBody(key, _character.Id);

        _packet.Read(stream);

        await Assert.That(_connection.ActiveChar).IsNull();
        await Assert.That(stream.Count).IsEqualTo(10);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        await AssertResponse(_character.Id, key, data);
        await Assert.That(_character.GetOption(key)).IsEqualTo(data);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_UsesOwnedNonactiveCharacterInsteadOfActiveCharacter()
    {
        var active = new Character(new UnitCustomModelParams(), _store) { Id = 43 };
        active.SetOption(7, "active-value");
        _connection.Characters.Add(active.Id, active);
        _connection.ActiveChar = active;

        _packet.Read(CreateBody(7, _character.Id));

        await AssertResponse(_character.Id, 7, "stored-value");
        await Assert.That(active.GetOption(7)).IsEqualTo("active-value");
        await Assert.That(_character.GetOption(7)).IsEqualTo("stored-value");
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Read_IgnoresUnknownCharacterId()
    {
        _connection.ActiveChar = _character;

        _packet.Read(CreateBody(7, 999));

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("stored-value");
    }

    [Test]
    public async Task Read_RejectsActiveCharacterAbsentFromOwnedCharacters()
    {
        _connection.ActiveChar = _character;
        _connection.Characters.Clear();

        _packet.Read(CreateBody(7, _character.Id));

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("stored-value");
    }

    [Test]
    [Arguments(4294967338UL)]
    [Arguments(ulong.MaxValue)]
    public async Task Read_Rejects64BitIdEvenWhenLowBitsAreOwned(ulong id)
    {
        _connection.Characters.Clear();
        _character.Id = unchecked((uint)id);
        _connection.Characters.Add(_character.Id, _character);
        _connection.ActiveChar = _character;

        _packet.Read(CreateBody(7, id));

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("stored-value");
    }

    [Test]
    public async Task Read_AcceptsUInt32MaxValueIn64BitIdField()
    {
        _connection.Characters.Clear();
        _character.Id = uint.MaxValue;
        _connection.Characters.Add(_character.Id, _character);

        _packet.Read(CreateBody(7, _character.Id));

        await AssertResponse(uint.MaxValue, 7, "stored-value");
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(8)]
    [Arguments(19)]
    [Arguments(21)]
    [Arguments(65535)]
    public async Task Read_RejectsUnsupportedType(int type)
    {
        var key = (ushort)type;
        _character.SetOption(key, "unsupported-value");

        _packet.Read(CreateBody(key, _character.Id));

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(key)).IsEqualTo("unsupported-value");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(9)]
    [Arguments(11)]
    [Arguments(12)]
    public async Task Read_RejectsTruncatedAndTrailingBodies(int length)
    {
        var bytes = CreateBody(7, _character.Id).GetBytes();
        Array.Resize(ref bytes, length);

        _packet.Read(new PacketStream(bytes));

        await Assert.That(_sentPackets.Count).IsEqualTo(0);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("stored-value");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Read_ReturnsEmptyForMissingOrEmptyOption(bool seedEmpty)
    {
        if (seedEmpty)
            _character.SetOption(20, "");

        _packet.Read(CreateBody(20, _character.Id));

        await AssertResponse(_character.Id, 20, "");
        await Assert.That(_character.GetOption(7)).IsEqualTo("stored-value");
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("nul")]
    [Arguments("high-surrogate")]
    [Arguments("low-surrogate")]
    [Arguments("ascii-overflow")]
    [Arguments("utf8-overflow")]
    [Arguments("null")]
    public async Task Read_ReturnsEmptyForInvalidPersistedDataWithoutChangingMemory(string kind)
    {
        var data = kind switch
        {
            "nul" => "before\0after",
            "high-surrogate" => "before\uD800after",
            "low-surrogate" => "before\uDC00after",
            "ascii-overflow" => new string('x', 8192),
            "utf8-overflow" => new string('x', 8188) + "\U0001F642",
            "null" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        _character.SetOption(7, data);

        _packet.Read(CreateBody(7, _character.Id));

        await AssertResponse(_character.Id, 7, "");
        await Assert.That(_character.GetOption(7)).IsEqualTo(data);
        await Assert.That(_store.Saves.Count).IsEqualTo(0);
    }

    private static PacketStream CreateBody(ushort type, ulong id) =>
        new PacketStream().Write(type).Write(id);

    private async Task AssertResponse(uint id, ushort type, string data)
    {
        await Assert.That(_sentPackets.Count).IsEqualTo(1);
        var stream = new PacketStream(_sentPackets[0]);
        var bytes = Encoding.UTF8.GetBytes(data);
        // Consume the current unencrypted GamePacket envelope before checking the UI body.
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)(stream.Count - 2));
        stream.ReadByte(); // Signature belongs to the transport, not the UI body.
        await Assert.That(stream.ReadByte()).IsEqualTo((byte)1);
        stream.ReadByte(); // Unused checksum.
        stream.ReadByte(); // Unused counter.
        await Assert.That(stream.ReadUInt16()).IsEqualTo(SCOffsets.SCResponseUIDataPacket);
        await Assert.That(stream.LeftBytes).IsEqualTo(16 + bytes.Length);
        await Assert.That(stream.ReadUInt64()).IsEqualTo((ulong)id);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(type);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)bytes.Length);
        await Assert.That(stream.ReadBytes(bytes.Length).SequenceEqual(bytes)).IsTrue();
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)bytes.Length);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
