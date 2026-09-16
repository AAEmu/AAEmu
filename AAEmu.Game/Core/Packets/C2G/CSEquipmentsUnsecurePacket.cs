using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// CS_PACKET_EQUIPMENTS_UNSECURE (0x07E) — "start unlocking everything I am wearing", raised by the
/// equipment half of the item-lock window. The client sends no body at all.
/// </summary>
public class CSEquipmentsUnsecurePacket() : GamePacket(CSOffsets.CSEquipmentsUnsecurePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
    }

    public override void Execute()
    {
        Logger.Debug("EquipmentsUnsecure");
        ItemManager.Instance.SetEquipmentsSecurity(Connection.ActiveChar, false);
    }
}
