using AAEmu.Commons.Network;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.UnitTests.Game.Core.Packets.G2C;

/// <summary>
/// The in-game event board's two answers. The count is the head the window sizes its list from, and the
/// empty packet is what clears it — the pair a server with no board events sends.
/// </summary>
public class EventBoardWireTests
{
    [Test]
    public async Task EventInfoCount_WritesTheCountAndTheLoadTime()
    {
        var stream = new SCEventInfoCountPacket(3, 1_700_000_000L).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(3u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(1_700_000_000L);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task EventInfoCount_WithNoEventsIsWhatAnEmptyBoardLooksLike()
    {
        // What the two callers send today: the server runs no board events, and the window still needs the
        // head at world entry or it dereferences an uninitialised list on show.
        var stream = new SCEventInfoCountPacket(0, 0).Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.ReadUInt32()).IsEqualTo(0u);
        await Assert.That(stream.ReadInt64()).IsEqualTo(0L);
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task EventEmpty_HasNoBodyAtAll()
    {
        var stream = new SCEventEmptyPacket().Write(new PacketStream());

        stream.Rollback();
        await Assert.That(stream.LeftBytes).IsEqualTo(0);
    }

    [Test]
    public async Task TheBoardPacketsAreDistinctOpcodes()
    {
        await Assert.That(new SCEventInfoCountPacket(0, 0).TypeId).IsEqualTo((ushort)0x2DD);
        await Assert.That(new SCEventEmptyPacket().TypeId).IsEqualTo((ushort)0x2DF);
    }
}
