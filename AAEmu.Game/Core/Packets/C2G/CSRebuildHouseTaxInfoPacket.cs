using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSRebuildHouseTaxInfoPacket() : GamePacket(CSOffsets.CSRebuildHouseTaxInfoPacket, 1)
{
    public short Tl { get; private set; }

    public override void Read(PacketStream stream)
    {
        Tl = stream.ReadInt16();

        // The client uses this request after changing the tax-panel state.  Timeline IDs are
        // allocated by HousingManager and therefore use the same lookup as the initial panel
        // request; do not derive a database or world-object ID from this value.
        if (Tl > 0)
            HousingManager.Instance.HouseTaxInfo(Connection, (ushort)Tl);
    }
}
