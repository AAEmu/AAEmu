using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class SlaveLeaveCancelTests
{
    [Test]
    public async Task CancelPendingLeave_NullToken_DoesNotThrow()
    {
        var slave = new Slave { ObjId = 2339 };
        slave.CancelPendingLeave();
        await Assert.That(slave.CancelTokenSource).IsNull();
    }

    [Test]
    public async Task CancelPendingLeave_ArmedToken_Cancels()
    {
        var slave = new Slave { ObjId = 2339, CancelTokenSource = new CancellationTokenSource() };
        slave.CancelPendingLeave();
        await Assert.That(slave.CancelTokenSource.IsCancellationRequested).IsTrue();
    }
}
