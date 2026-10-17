using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRepairAllEquipmentsPacket() : GamePacket(CSOffsets.CSRepairAllEquipmentsPacket, 1)
{
    public override void Read(PacketStream stream)
    {
        var autoUseAAPoint = stream.ReadBoolean();
        // inBag is bag-repair *authority* (Patron / fset path), not "repair the bag container".
        // The client collector only walks paper-doll slots — always repair Equipment.
        var inBag = stream.ReadBoolean();

        Logger.Debug("RepairAllEquipments, AutoUseAAPoint: {0}, InBag: {1}", autoUseAAPoint, inBag);

        Connection.ActiveChar.DoRepair(
            [.. Connection.ActiveChar.Inventory.Equipment.Items],
            autoUseAAPoint);
    }
}
