using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSDominionUpdateTaxRatePacket : GamePacket
{
    public CSDominionUpdateTaxRatePacket() : base(CSOffsets.CSDominionUpdateTaxRatePacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        var id = stream.ReadUInt16();
        var taxRate = stream.ReadInt32();

        Logger.Debug($"DominionUpdateTaxRate, Id: {id}, TaxRate: {taxRate}");
    }
}
