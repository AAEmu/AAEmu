using AAEmu.Commons.Network;

namespace AAEmu.UnitTests.Commons.Network;

public class LengthPrefixedFramesTests
{
    [Test]
    public async Task OneByteLeftover_WaitsWithoutConsuming()
    {
        PacketStream? stream = new PacketStream();
        stream.Insert(0, [0x01]);

        var result = LengthPrefixedFrames.TryTake(ref stream, 2, out var frame);

        await Assert.That(result).IsEqualTo(LengthPrefixedFrameResult.NeedMore);
        await Assert.That(frame).IsNull();
        await Assert.That(stream).IsNotNull();
        await Assert.That(stream!.Count).IsEqualTo(1);
        await Assert.That(stream.Pos).IsEqualTo(0);
    }

    [Test]
    public async Task ZeroPayloadLength_IsDroppedNotDispatched()
    {
        PacketStream? stream = new PacketStream();
        stream.Insert(0, [0x00, 0x00]);

        var result = LengthPrefixedFrames.TryTake(ref stream, 2, out var frame);

        await Assert.That(result).IsEqualTo(LengthPrefixedFrameResult.DroppedInvalidLength);
        await Assert.That(frame).IsNull();
        await Assert.That(stream).IsNull();
    }

    [Test]
    public async Task ValidFrame_SlicesPayloadAndLeavesRemainder()
    {
        PacketStream? stream = new PacketStream();
        // payloadLen=4 (type u16 + 2 body bytes), then leftover 0x99
        stream.Insert(0, [0x04, 0x00, 0x2A, 0x00, 0x01, 0x02, 0x99]);

        var result = LengthPrefixedFrames.TryTake(ref stream, 2, out var frame);

        await Assert.That(result).IsEqualTo(LengthPrefixedFrameResult.GotFrame);
        await Assert.That(frame).IsNotNull();
        await Assert.That(frame!.Count).IsEqualTo(6);
        await Assert.That(stream).IsNotNull();
        await Assert.That(stream!.Count).IsEqualTo(1);
        await Assert.That(stream.Buffer[0]).IsEqualTo((byte)0x99);
    }

    [Test]
    public async Task PartialPayload_WaitsWithPosReset()
    {
        PacketStream? stream = new PacketStream();
        // payloadLen=8 but only 2 payload bytes present
        stream.Insert(0, [0x08, 0x00, 0x2A, 0x00]);

        var result = LengthPrefixedFrames.TryTake(ref stream, 2, out var frame);

        await Assert.That(result).IsEqualTo(LengthPrefixedFrameResult.NeedMore);
        await Assert.That(frame).IsNull();
        await Assert.That(stream!.Count).IsEqualTo(4);
        await Assert.That(stream.Pos).IsEqualTo(0);
    }

    [Test]
    public async Task RepeatedOneByteTake_DoesNotSpin()
    {
        PacketStream? stream = new PacketStream();
        stream.Insert(0, [0x01]);

        for (var i = 0; i < 1000; i++)
        {
            var result = LengthPrefixedFrames.TryTake(ref stream, 2, out _);
            await Assert.That(result).IsEqualTo(LengthPrefixedFrameResult.NeedMore);
        }

        await Assert.That(stream!.Count).IsEqualTo(1);
    }
}
