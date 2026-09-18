using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <summary>
/// The remodel window's build-cost request. It carries a house timeline id, but the window itself always
/// opens from the house that is already being worked on and sends <c>0xFFFF</c> instead of one — so the id
/// is a hint, not a requirement, and a request without one is answered for that house with
/// <c>SCRebuildHouseTaxInfoPacket</c>.
/// </summary>
/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value: one u16.
/// </remarks>
public class CSRebuildHouseTaxInfoPacket() : GamePacket(CSOffsets.CSRebuildHouseTaxInfoPacket, 1)
{
    public ushort Tl { get; private set; }

    public override void Read(PacketStream stream)
    {
        Tl = stream.ReadUInt16();

        HousingManager.Instance.HouseRebuildTaxInfo(Connection, Tl);
    }
}
