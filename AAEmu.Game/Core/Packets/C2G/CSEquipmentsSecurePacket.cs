using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// CS_PACKET_EQUIPMENTS_SECURE (0x07D) — "lock everything I am wearing", raised by the equipment
/// half of the item-lock window. The client sends no body at all.
/// </summary>
public class CSEquipmentsSecurePacket() : GamePacket(CSOffsets.CSEquipmentsSecurePacket, 1)
{
    public override void Read(PacketStream stream)
    {
        // Empty struct
    }

    public override void Execute()
    {
        Logger.Debug("EquipmentsSecure");
        ItemManager.Instance.SetEquipmentsSecurity(Connection.ActiveChar, true);
    }
}
