using AAEmu.Commons.Utils.AAPak;

namespace AAEmu.UnitTests.Commons.Utils.AAPak;

public class SubStreamTests
{
    [Test]
    public async Task Read_IsBoundedToSelectedRange()
    {
        using var source = new MemoryStream([0, 1, 2, 3, 4, 5]);
        using var stream = new SubStream(source, 2, 3);
        var buffer = new byte[8];

        var read = stream.Read(buffer, 0, buffer.Length);

        await Assert.That(read).IsEqualTo(3);
        await Assert.That(buffer[..read]).IsEquivalentTo(new byte[] { 2, 3, 4 });
        await Assert.That(stream.Read(buffer, 0, buffer.Length)).IsEqualTo(0);
    }

    [Test]
    public async Task Seek_RemainsInsideSelectedRange()
    {
        using var source = new MemoryStream([0, 1, 2, 3, 4, 5]);
        using var stream = new SubStream(source, 1, 4);

        await Assert.That(stream.Seek(-1, SeekOrigin.End)).IsEqualTo(3L);
        await Assert.That(stream.ReadByte()).IsEqualTo(4);
        stream.Position = 99;
        await Assert.That(stream.Position).IsEqualTo(4L);
    }

    [Test]
    public async Task Constructor_RejectsRangePastEnd()
    {
        using var source = new MemoryStream([0, 1, 2]);
        await Assert.That(() => new SubStream(source, 2, 2)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Write_IsExplicitlyUnsupported()
    {
        using var source = new MemoryStream([0, 1, 2]);
        using var stream = new SubStream(source, 0, 3);
        await Assert.That(() => stream.Write([9], 0, 1)).Throws<NotSupportedException>();
    }
}
