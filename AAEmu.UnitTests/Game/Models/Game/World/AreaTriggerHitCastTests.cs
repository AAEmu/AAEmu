using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.UnitTests.Game.Models.Game.World;

public class AreaTriggerHitCastTests
{
    [Test]
    public async Task PlotSummon_ReportsTheHitAsThatPlot()
    {
        var cast = AreaTrigger.CreateHitCast(9, 101, 4, 3);
        var stream = new PacketStream();
        cast.Write(stream);
        var bytes = stream.GetBytes();

        await Assert.That(bytes[0]).IsEqualTo((byte)1);
        await Assert.That(BitConverter.ToUInt32(bytes, 1)).IsEqualTo(9u);
    }

    [Test]
    public async Task NoPlot_ReportsTheHitAsASkill()
    {
        var cast = AreaTrigger.CreateHitCast(0, 7, 0, 12);
        var stream = new PacketStream();
        cast.Write(stream);

        await Assert.That(stream.GetBytes()[0]).IsEqualTo((byte)0);
    }
}
