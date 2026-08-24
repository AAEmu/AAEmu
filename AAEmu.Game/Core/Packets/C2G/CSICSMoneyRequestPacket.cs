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

        var points = AccountManager.Instance.GetAccountDetails(Connection.AccountId);
        Connection.SendPacket(new SCICSCashPointPacket(points.Credits, points.Loyalty));
    }
}
