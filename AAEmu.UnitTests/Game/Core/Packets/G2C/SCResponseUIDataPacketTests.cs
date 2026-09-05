using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCResponseUIDataPacketTests
{
    [Test]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    [Arguments(6)]
    [Arguments(7)]
    [Arguments(20)]
    public async Task Write_SupportedTypesUse64BitIdAndMatchingByteLengths(int type)
    {
        const string data = "a\u00E9\u4E2D\U0001F642z";
        var key = (ushort)type;
        var stream = new SCResponseUIDataPacket(uint.MaxValue, key, data).Write(new PacketStream());

        await AssertBody(stream, uint.MaxValue, key, Encoding.UTF8.GetBytes(data));
    }

    [Test]
    [Arguments("")]
    [Arguments(" \t{ \"unknown_test_field\" : [ 9, \"opaque\" ] }\r\n ")]
    [Arguments("not a parsed document: \U0001F642\r\n")]
    public async Task Write_PreservesOpaqueBytesWhitespaceAndEmptyValues(string data)
    {
        var stream = new SCResponseUIDataPacket(42, 7, data).Write(new PacketStream());

        await AssertBody(stream, 42, 7, Encoding.UTF8.GetBytes(data));
    }

    [Test]
    [Arguments(8191, false)]
    [Arguments(8191, true)]
    [Arguments(8192, false)]
    [Arguments(8192, true)]
    public async Task Write_EnforcesByteLimitRatherThanCharacterLimit(int byteLength, bool supplementary)
    {
        var data = supplementary ? new string('x', byteLength - 4) + "\U0001F642" : new string('x', byteLength);
        var bytes = Encoding.UTF8.GetBytes(data);

        var stream = new SCResponseUIDataPacket(42, 20, data).Write(new PacketStream());

        await Assert.That(bytes.Length).IsEqualTo(byteLength);
        await AssertBody(stream, 42, 20, byteLength == 8191 ? bytes : []);
    }

    [Test]
    [Arguments(null)]
    [Arguments("\0")]
    [Arguments("\0value")]
    [Arguments("before\0after")]
    [Arguments("value\0")]
    public async Task Write_InvalidStoredTextProducesExactlyEmptyBody(string data)
    {
        var stream = new SCResponseUIDataPacket(42, 7, data).Write(new PacketStream());

        await AssertBody(stream, 42, 7, []);
    }

    [Test]
    [Arguments(0xD800)]
    [Arguments(0xDBFF)]
    [Arguments(0xDC00)]
    [Arguments(0xDFFF)]
    public async Task Write_InvalidUtf16ProducesEmptyRatherThanReplacementBytes(int codeUnit)
    {
        // Construct unpaired surrogates at runtime, not in attribute metadata.
        var data = "before" + (char)codeUnit + "after";

        var stream = new SCResponseUIDataPacket(42, 7, data).Write(new PacketStream());

        await AssertBody(stream, 42, 7, []);
    }

    [Test]
    [Arguments(0)]
    [Arguments(8)]
    [Arguments(19)]
    [Arguments(21)]
    [Arguments(65535)]
    public async Task Write_UnsupportedTypeThrowsBeforeWriting(int type)
    {
        var stream = new PacketStream();
        var packet = new SCResponseUIDataPacket(42, (ushort)type, "opaque");

        await Assert.That(() => packet.Write(stream)).Throws<ArgumentOutOfRangeException>();

        await Assert.That(stream.Count).IsEqualTo(0);
    }

    private static async Task AssertBody(PacketStream stream, uint id, ushort type, byte[] bytes)
    {
        stream.Rollback();
        await Assert.That(stream.Count).IsEqualTo(16 + bytes.Length);
        await Assert.That(stream.ReadUInt64()).IsEqualTo((ulong)id);
        await Assert.That(stream.ReadUInt16()).IsEqualTo(type);
        await Assert.That(stream.ReadUInt16()).IsEqualTo((ushort)bytes.Length);
        await Assert.That(stream.ReadBytes(bytes.Length).SequenceEqual(bytes)).IsTrue();
        await Assert.That(stream.ReadUInt32()).IsEqualTo((uint)bytes.Length);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }
}
