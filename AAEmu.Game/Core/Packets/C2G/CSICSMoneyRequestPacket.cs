using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSICSMoneyRequestPacket() : GamePacket(CSOffsets.CSICSMoneyRequestPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        Logger.Info("ICSMoneyRequest account={0}", Connection.AccountId);

        var credits = 0;
        var loyalty = 0;

        if (BillClientManager.Instance.IsConnected)
        {
            var accountName = Connection.ActiveChar?.Name ?? $"account{Connection.AccountId}";
            var charId = Connection.ActiveChar?.Id ?? 0;
            var billCash = BillClientManager.Instance
                .TryGetCashAsync(Connection.AccountId, accountName, charId)
                .GetAwaiter()
                .GetResult();
            if (billCash is not null)
            {
                credits = Math.Max(0, billCash.Value.Cash);
                loyalty = AccountManager.Instance.GetAccountDetails(Connection.AccountId).Loyalty;
                Connection.SendPacket(new SCICSCashPointPacket(credits, loyalty));
                return;
            }
        }

        var points = AccountManager.Instance.GetAccountDetails(Connection.AccountId);
        Connection.SendPacket(new SCICSCashPointPacket(points.Credits, points.Loyalty));
    }
}
