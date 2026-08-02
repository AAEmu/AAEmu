using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSCofferSubbagOpenPacket() : GamePacket(CSOffsets.CSCofferSubbagOpenPacket, 1)
{
    public long ItemId { get; private set; }
    public bool Start { get; private set; }

    public override void Read(PacketStream stream)
    {
        ItemId = stream.ReadInt64();
        Start = stream.ReadBoolean();

        if (ItemId <= 0 ||
            !DoodadManager.Instance.SetCofferSubbagOpen(Connection.ActiveChar, (ulong)ItemId, Start))
        {
            Logger.Warn($"{Connection.ActiveChar?.Name} failed to {(Start ? "open" : "close")} coffer subbag {ItemId}");
        }
    }
}
