using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>Returns the current purchase counters for limited Marketplace goods.</summary>
public class CSICSBuyCountRequestPacket() : GamePacket(CSOffsets.CSICSBuyCountRequestPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var pk = stream.ReadByte();
        // pk mirrors SC kind: 1 = ICS shop products
        var kind = pk == 0 ? 1u : pk;
        Logger.Debug("CSICSBuyCountRequest pk={0}", pk);

        var character = Connection.ActiveChar;
        if (character == null)
            return;

        var entries = CashShopManager.Instance.BuildBuyCountEntries(character.AccountId, character.Id);
        Connection.SendPacket(new SCICSBuyCountPacket(kind, entries));
    }
}
