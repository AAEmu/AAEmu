using System.Text;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Music;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

public class SCUserNoteLoadedPacketTests
{
    [Test]
    public async Task Write_WritesScoreItemIdTooltipAndContainerBeforeLengths()
    {
        var body = new SCUserNoteLoadedPacket(0x0A0B0C0D, true, 3, "My Score", "abc")
            .Write(new PacketStream())
            .GetBytes();

        var expected = new PacketStream();
        expected.Write(0x0A0B0C0Du);
        expected.Write(true);
        expected.Write((sbyte)3);
        expected.Write(3u);
        expected.Write("My Score");
        expected.Write("abc\0");

        // The container byte is the field the score window reads back: without it every value
        // behind it is shifted by one and the client parses garbage.
        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Write_CountsTheScoreInBytesAndKeepsItsTerminator()
    {
        const string notes = "c1 d2 e3";
        var packet = new SCUserNoteLoadedPacket(7, false, 2, "T", notes);
        var body = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(packet.NoteLength).IsEqualTo((uint)Encoding.UTF8.GetByteCount(notes));

        var expected = new PacketStream();
        expected.Write(7u);
        expected.Write(false);
        expected.Write((sbyte)2);
        expected.Write((uint)notes.Length);
        expected.Write("T");
        expected.Write(notes, true, true); // the score keeps its null terminator

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Write_MeasuresMultibyteScoresInBytes()
    {
        const string notes = "도레미"; // three characters, nine bytes
        var packet = new SCUserNoteLoadedPacket(7, false, 2, "제목", notes);
        var body = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(packet.NoteLength).IsEqualTo(9u);

        var expected = new PacketStream();
        expected.Write(7u);
        expected.Write(false);
        expected.Write((sbyte)2);
        expected.Write(9u);
        expected.Write("제목");
        expected.Write(notes, true, true);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Write_ClampsATitleThatDoesNotFitTheClientBuffer()
    {
        var title = new string('♪', 40); // 120 bytes, over the 96 byte title buffer
        var packet = new SCUserNoteLoadedPacket(1, false, 0, title, "x");
        var body = packet.Write(new PacketStream()).GetBytes();

        var expected = new PacketStream();
        expected.Write(1u);
        expected.Write(false);
        expected.Write((sbyte)0);
        expected.Write(1u);
        expected.Write(new string('♪', 32)); // clamped on a character boundary
        expected.Write("x", true, true);

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Write_SendsAnEmptyNoteForAScoreWithoutNotes()
    {
        var packet = new SCUserNoteLoadedPacket(42, false, 0, string.Empty, string.Empty);
        var body = packet.Write(new PacketStream()).GetBytes();

        await Assert.That(packet.NoteLength).IsEqualTo(0u);

        var expected = new PacketStream();
        expected.Write(42u);
        expected.Write(false);
        expected.Write((sbyte)0);
        expected.Write(0u);
        expected.Write(string.Empty);
        expected.Write("\0");

        await Assert.That(body).IsEquivalentTo(expected.GetBytes());
    }

    [Test]
    public async Task Write_EchoesTheContainerByteOfTheRequest()
    {
        // The client asks from the bag (2) and from the warehouse (3); both are echoed back as one
        // opaque byte, so neither may be reinterpreted on the way out.
        var bag = new SCUserNoteLoadedPacket(5, true, 2, "t", "n").Write(new PacketStream()).GetBytes();
        var warehouse = new SCUserNoteLoadedPacket(5, true, 3, "t", "n").Write(new PacketStream()).GetBytes();

        await Assert.That(bag[5]).IsEqualTo((byte)2);
        await Assert.That(warehouse[5]).IsEqualTo((byte)3);
    }
}
