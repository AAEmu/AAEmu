using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Packets.C2G;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Packets.C2G;

public class CSSaveUIDataPacketTests
{
    private readonly UiDataPacketTestStore _store = new();
    private readonly GameConnection _connection = new(Mock.Of<ISession>().Object);
    private readonly Character _character;
    private readonly CSSaveUIDataPacket _packet;

    public CSSaveUIDataPacketTests()
    {
        _character = new Character(new UnitCustomModelParams(), _store) { Id = 42 };
        _character.SetOption(7, "original-value");
        _connection.Characters.Add(_character.Id, _character);
        _packet = new CSSaveUIDataPacket { Connection = _connection };
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
    public async Task Read_UsesPacketCharacterWhenActiveCharacterIsNull(int type)
    {
        var key = (ushort)type;
        var stream = CreateBody(key, _character.Id, Encoding.UTF8.GetBytes("saved-value"));

        _packet.Read(stream);

        await Assert.That(_connection.ActiveChar).IsNull();
        await Assert.That(_character.GetOption(key)).IsEqualTo("saved-value");
        await Assert.That(_store.Saves.Count).IsEqualTo(1);
        await Assert.That(_store.Saves[0]).IsEqualTo((_character.Id, key, "saved-value"));
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task Read_UsesOwnedNonactiveCharacterInsteadOfActiveCharacter()
    {
        var active = new Character(new UnitCustomModelParams(), _store) { Id = 43 };
        active.SetOption(7, "active-value");
        _connection.Characters.Add(active.Id, active);
        _connection.ActiveChar = active;

        _packet.Read(CreateBody(7, _character.Id, Encoding.UTF8.GetBytes("saved-value")));

        await Assert.That(_character.GetOption(7)).IsEqualTo("saved-value");
        await Assert.That(active.GetOption(7)).IsEqualTo("active-value");
        await Assert.That(_store.Saves.Count).IsEqualTo(1);
        await Assert.That(_store.Saves[0]).IsEqualTo((_character.Id, (ushort)7, "saved-value"));
    }

    [Test]
    public async Task Read_IgnoresUnknownCharacterId()
    {
        _connection.ActiveChar = _character;

        _packet.Read(CreateBody(7, 999, Encoding.UTF8.GetBytes("saved-value")));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    [Test]
    public async Task Read_RejectsActiveCharacterAbsentFromOwnedCharacters()
    {
        _connection.ActiveChar = _character;
        _connection.Characters.Clear();

        _packet.Read(CreateBody(7, _character.Id, Encoding.UTF8.GetBytes("saved-value")));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
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

        _packet.Read(CreateBody(7, id, Encoding.UTF8.GetBytes("saved-value")));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    [Test]
    public async Task Read_AcceptsUInt32MaxValueIn64BitIdField()
    {
        _connection.Characters.Clear();
        _character.Id = uint.MaxValue;
        _connection.Characters.Add(_character.Id, _character);

        _packet.Read(CreateBody(7, _character.Id, Encoding.UTF8.GetBytes("saved-value")));

        await Assert.That(_character.GetOption(7)).IsEqualTo("saved-value");
        await Assert.That(_store.Saves.Count).IsEqualTo(1);
        await Assert.That(_store.Saves[0]).IsEqualTo((uint.MaxValue, (ushort)7, "saved-value"));
    }

    [Test]
    [Arguments(0)]
    [Arguments(8)]
    [Arguments(19)]
    [Arguments(21)]
    [Arguments(65535)]
    public async Task Read_RejectsUnsupportedTypeWithoutPersistence(int type)
    {
        var key = (ushort)type;
        _character.SetOption(key, "unsupported-original");

        _packet.Read(CreateBody(key, _character.Id, Encoding.UTF8.GetBytes("saved-value")));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(key)).IsEqualTo("unsupported-original");
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(9)]
    [Arguments(10)]
    [Arguments(11)]
    public async Task Read_RejectsTruncatedHeaderWithoutPersistence(int length)
    {
        var body = CreateBody(7, _character.Id, []).GetBytes();

        _packet.Read(new PacketStream(body[..length]));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    [Test]
    [Arguments(0)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(5)]
    [Arguments(65535)]
    public async Task Read_RejectsLengthMismatchIncludingUtf16LengthAndTrailingBytes(int declaredLength)
    {
        // One supplementary character is two UTF-16 code units but four UTF-8 bytes.
        var bytes = Encoding.UTF8.GetBytes("\U0001F642");

        _packet.Read(CreateBody(7, _character.Id, bytes, (ushort)declaredLength));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    [Test]
    public async Task Read_Rejects32BitIdLayoutWithoutPersistence()
    {
        var stream = new PacketStream();
        stream.Write((ushort)7);
        stream.Write(_character.Id);
        stream.Write((ushort)5);
        stream.Write(Encoding.UTF8.GetBytes("value"), false);

        _packet.Read(stream);

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    [Test]
    [Arguments("")]
    [Arguments(" \t{ \"unknown_test_field\" : [ 9, \"opaque\" ] }\r\n ")]
    [Arguments("a\u00E9\u4E2D\U0001F642z")]
    public async Task Read_PreservesOpaqueTextAndEmptyValues(string data)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        var stream = CreateBody(7, _character.Id, bytes);

        _packet.Read(stream);

        await Assert.That(stream.Count).IsEqualTo(12 + bytes.Length);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo(data);
        await Assert.That(_store.Saves.Count).IsEqualTo(1);
        await Assert.That(_store.Saves[0]).IsEqualTo((_character.Id, (ushort)7, data));
    }

    [Test]
    [Arguments(8191, false)]
    [Arguments(8191, true)]
    [Arguments(8192, false)]
    [Arguments(8192, true)]
    public async Task Read_EnforcesByteLimitRatherThanCharacterLimit(int byteLength, bool supplementary)
    {
        var data = supplementary ? new string('x', byteLength - 4) + "\U0001F642" : new string('x', byteLength);
        var bytes = Encoding.UTF8.GetBytes(data);

        _packet.Read(CreateBody(7, _character.Id, bytes));

        await Assert.That(bytes.Length).IsEqualTo(byteLength);
        await Assert.That(_store.Saves.Count).IsEqualTo(byteLength == 8191 ? 1 : 0);
        await Assert.That(_character.GetOption(7)).IsEqualTo(byteLength == 8191 ? data : "original-value");
        if (byteLength == 8191)
            await Assert.That(_store.Saves[0]).IsEqualTo((_character.Id, (ushort)7, data));
    }

    [Test]
    [Arguments("80")]
    [Arguments("C0AF")]
    [Arguments("E282")]
    [Arguments("EDA080")]
    [Arguments("F4908080")]
    [Arguments("F09F99")]
    [Arguments("00")]
    [Arguments("0061")]
    [Arguments("610062")]
    [Arguments("6100")]
    public async Task Read_RejectsInvalidUtf8AndNulWithoutPersistence(string hex)
    {
        _packet.Read(CreateBody(7, _character.Id, Convert.FromHexString(hex)));

        await Assert.That(_store.Saves.Count).IsEqualTo(0);
        await Assert.That(_character.GetOption(7)).IsEqualTo("original-value");
    }

    private static PacketStream CreateBody(ushort uiDataType, ulong characterId, byte[] data, ushort? declaredLength = null)
    {
        var stream = new PacketStream();
        stream.Write(uiDataType);
        stream.Write(characterId);
        stream.Write(declaredLength ?? (ushort)data.Length);
        stream.Write(data, false);
        stream.Rollback();
        return stream;
    }
}
