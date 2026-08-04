using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

/// <remarks>
/// Field order, widths and names come from the 10.0.2.13 client's serializer, which passes each
/// value's name alongside the value:
/// </remarks>
public class CSPrepayHouseTaxPacket() : GamePacket(CSOffsets.CSPrepayHouseTaxPacket, 1)
{
    public short Tl { get; private set; }
    public bool Ausp { get; private set; }

    public override void Read(PacketStream stream)
    {
        Tl = stream.ReadInt16();
        Ausp = stream.ReadBoolean();

        if (Tl > 0)
            HousingManager.Instance.PrepayHouseTax(Connection, (ushort)Tl, Ausp);
    }
}
