using AAEmu.Game.Core.Network.Bill;

namespace AAEmu.UnitTests.Game.Core.Network.Bill;

public class BillWireTests
{
    [Test]
    public async Task EncodeJoinFrame_HasExpectedLengthPrefix()
    {
        var body = new BillWriter();
        body.WriteI32(4);
        body.WriteI32(1);
        body.WriteU8(1);
        body.WriteI32(0);
        var bodyBytes = body.ToArray();

        var frame = BillFrame.Encode(BillOpcodes.Join, bodyBytes);

        await Assert.That(frame.Length).IsEqualTo(2 + 2 + bodyBytes.Length);
        await Assert.That(frame[0]).IsEqualTo((byte)(2 + bodyBytes.Length));
    }
}
